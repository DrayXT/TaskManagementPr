using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using TaskManagementPr.Models;
using TaskManagementPr.Services;

namespace TaskManagementPr.Data
{
    public class ProjectMemberRepository
    {
        private bool _hasBeenInitialized;
        private readonly ILogger<ProjectMemberRepository> _logger;
        private readonly IAuthService _authService;

        public ProjectMemberRepository(IAuthService authService, ILogger<ProjectMemberRepository> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

        private async Task Init()
        {
            if (_hasBeenInitialized)
                return;

            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            try
            {
                var createTableCmd = connection.CreateCommand();
                createTableCmd.CommandText = @"
CREATE TABLE IF NOT EXISTS ProjectMember (
    ID INTEGER PRIMARY KEY AUTOINCREMENT,
    ProjectID INTEGER NOT NULL,
    UserEmail TEXT NOT NULL COLLATE NOCASE,
    IsOwner INTEGER NOT NULL DEFAULT 0,
    IsPending INTEGER NOT NULL DEFAULT 0,
    UNIQUE(ProjectID, UserEmail)
);";
                await createTableCmd.ExecuteNonQueryAsync();
                await EnsureOwnersForProjectsWithoutMembersAsync(connection);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error creating ProjectMember table");
                throw;
            }

            _hasBeenInitialized = true;
        }

        private async Task EnsureOwnersForProjectsWithoutMembersAsync(SqliteConnection connection)
        {
            var fallback = string.IsNullOrWhiteSpace(_authService.CurrentUserEmail)
                ? "local@owner.app"
                : NormalizeEmail(_authService.CurrentUserEmail);

            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT ID FROM Project";
            var ids = new List<int>();
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    ids.Add(reader.GetInt32(0));
            }

            foreach (var pid in ids)
            {
                var countCmd = connection.CreateCommand();
                countCmd.CommandText = "SELECT COUNT(*) FROM ProjectMember WHERE ProjectID = @pid";
                countCmd.Parameters.AddWithValue("@pid", pid);
                var count = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
                if (count > 0)
                    continue;

                await InsertMemberAsync(connection, pid, fallback, isOwner: true, isPending: false);
            }
        }

        public async Task<List<ProjectMember>> ListByProjectAsync(int projectId)
        {
            await Init();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var selectCmd = connection.CreateCommand();
            selectCmd.CommandText =
                "SELECT ID, ProjectID, UserEmail, IsOwner, IsPending FROM ProjectMember WHERE ProjectID = @pid ORDER BY IsOwner DESC, UserEmail";
            selectCmd.Parameters.AddWithValue("@pid", projectId);
            var list = new List<ProjectMember>();
            await using var reader = await selectCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new ProjectMember
                {
                    ID = reader.GetInt32(0),
                    ProjectID = reader.GetInt32(1),
                    UserEmail = reader.GetString(2),
                    IsOwner = reader.GetBoolean(3),
                    IsPending = reader.GetBoolean(4)
                });
            }

            return list;
        }

        public async Task ActivatePendingForEmailAsync(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return;

            await Init();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var cmd = connection.CreateCommand();
            cmd.CommandText =
                "UPDATE ProjectMember SET IsPending = 0 WHERE UserEmail = @e COLLATE NOCASE AND IsPending = 1";
            cmd.Parameters.AddWithValue("@e", NormalizeEmail(email));
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<int> CountMembersAsync(int projectId)
        {
            await Init();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM ProjectMember WHERE ProjectID = @pid";
            cmd.Parameters.AddWithValue("@pid", projectId);
            var scalar = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(scalar);
        }

        public async Task EnsureOwnerAsync(int projectId, string? preferredEmail)
        {
            if (await CountMembersAsync(projectId) > 0)
                return;

            var email = string.IsNullOrWhiteSpace(preferredEmail)
                ? (_authService.CurrentUserEmail is { } u ? NormalizeEmail(u) : "local@owner.app")
                : NormalizeEmail(preferredEmail);

            await AddMemberInternalAsync(projectId, email, isOwner: true, isPending: false);
        }

        public async Task InviteOrAddMemberAsync(int projectId, string email)
        {
            await Init();
            var normalized = NormalizeEmail(email);
            if (string.IsNullOrEmpty(normalized))
                return;

            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            if (await MemberExistsAsync(connection, projectId, normalized))
                return;

            var current = _authService.CurrentUserEmail is { } c ? NormalizeEmail(c) : null;
            var pending = current is null || !string.Equals(normalized, current, StringComparison.Ordinal);
            await InsertMemberAsync(connection, projectId, normalized, isOwner: false, isPending: pending);
        }

        private static async Task<bool> MemberExistsAsync(SqliteConnection connection, int projectId, string normalizedEmail)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText =
                "SELECT 1 FROM ProjectMember WHERE ProjectID = @pid AND UserEmail = @e COLLATE NOCASE LIMIT 1";
            cmd.Parameters.AddWithValue("@pid", projectId);
            cmd.Parameters.AddWithValue("@e", normalizedEmail);
            var o = await cmd.ExecuteScalarAsync();
            return o is not null && o is not DBNull;
        }

        private async Task AddMemberInternalAsync(int projectId, string normalizedEmail, bool isOwner, bool isPending)
        {
            await Init();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();
            await InsertMemberAsync(connection, projectId, normalizedEmail, isOwner, isPending);
        }

        private static async Task InsertMemberAsync(SqliteConnection connection, int projectId, string normalizedEmail,
            bool isOwner, bool isPending)
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText =
                "INSERT INTO ProjectMember (ProjectID, UserEmail, IsOwner, IsPending) VALUES (@pid, @email, @owner, @pending)";
            cmd.Parameters.AddWithValue("@pid", projectId);
            cmd.Parameters.AddWithValue("@email", normalizedEmail);
            cmd.Parameters.AddWithValue("@owner", isOwner ? 1 : 0);
            cmd.Parameters.AddWithValue("@pending", isPending ? 1 : 0);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task RemoveMemberAsync(ProjectMember member)
        {
            await Init();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM ProjectMember WHERE ID = @id";
            cmd.Parameters.AddWithValue("@id", member.ID);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task TransferOwnershipAsync(int projectId, string newOwnerEmail)
        {
            await Init();
            var email = NormalizeEmail(newOwnerEmail);
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();
            await using var tx = connection.BeginTransaction();

            try
            {
                var clear = connection.CreateCommand();
                clear.Transaction = tx;
                clear.CommandText = "UPDATE ProjectMember SET IsOwner = 0 WHERE ProjectID = @pid";
                clear.Parameters.AddWithValue("@pid", projectId);
                await clear.ExecuteNonQueryAsync();

                var set = connection.CreateCommand();
                set.Transaction = tx;
                set.CommandText =
                    "UPDATE ProjectMember SET IsOwner = 1, IsPending = 0 WHERE ProjectID = @pid AND UserEmail = @e COLLATE NOCASE";
                set.Parameters.AddWithValue("@pid", projectId);
                set.Parameters.AddWithValue("@e", email);
                await set.ExecuteNonQueryAsync();

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public async Task DeleteAllForProjectAsync(int projectId)
        {
            await Init();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();
            var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM ProjectMember WHERE ProjectID = @pid";
            cmd.Parameters.AddWithValue("@pid", projectId);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task DropTableAsync()
        {
            if (!_hasBeenInitialized)
            {
                await using var connection = new SqliteConnection(Constants.DatabasePath);
                await connection.OpenAsync();
                var drop = connection.CreateCommand();
                drop.CommandText = "DROP TABLE IF EXISTS ProjectMember";
                await drop.ExecuteNonQueryAsync();
                return;
            }

            await using var connection2 = new SqliteConnection(Constants.DatabasePath);
            await connection2.OpenAsync();
            var drop2 = connection2.CreateCommand();
            drop2.CommandText = "DROP TABLE IF EXISTS ProjectMember";
            await drop2.ExecuteNonQueryAsync();
            _hasBeenInitialized = false;
        }

        public bool IsCurrentUserOwner(int projectId, IReadOnlyList<ProjectMember> members)
        {
            var me = _authService.CurrentUserEmail is { } e ? NormalizeEmail(e) : null;
            if (me is null)
                return members.Any(m => m.IsOwner && m.UserEmail.Equals("local@owner.app", StringComparison.OrdinalIgnoreCase));

            return members.Any(m => m.IsOwner && m.UserEmail.Equals(me, StringComparison.OrdinalIgnoreCase));
        }

        public bool IsCurrentUserMember(IReadOnlyList<ProjectMember> members)
        {
            var me = _authService.CurrentUserEmail is { } e ? NormalizeEmail(e) : null;
            if (me is null)
                return true;

            return members.Any(m =>
                !m.IsPending && m.UserEmail.Equals(me, StringComparison.OrdinalIgnoreCase));
        }
    }
}
