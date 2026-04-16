using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using TaskManagementPr.Models;
using TaskManagementPr.Services;

namespace TaskManagementPr.Data
{
    public class ProjectMemberRepository
    {
        private const string LocalOwnerPlaceholderEmail = "local@owner.app";
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
            var currentEmail = await _authService.GetEmailAsync();
            var fallback = string.IsNullOrWhiteSpace(currentEmail)
                ? LocalOwnerPlaceholderEmail
                : NormalizeEmail(currentEmail);

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
            var normalized = NormalizeEmail(email);
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var cmd = connection.CreateCommand();
            cmd.CommandText =
                "UPDATE ProjectMember SET IsPending = 0 WHERE UserEmail = @e COLLATE NOCASE AND IsPending = 1";
            cmd.Parameters.AddWithValue("@e", normalized);
            await cmd.ExecuteNonQueryAsync();

            await PromoteLegacyLocalOwnerToCurrentUserAsync(connection, normalized);
        }

        private static async Task PromoteLegacyLocalOwnerToCurrentUserAsync(SqliteConnection connection, string normalizedCurrentEmail)
        {
            var localProjectsCmd = connection.CreateCommand();
            localProjectsCmd.CommandText = @"
SELECT DISTINCT ProjectID
FROM ProjectMember
WHERE UserEmail = @local COLLATE NOCASE";
            localProjectsCmd.Parameters.AddWithValue("@local", LocalOwnerPlaceholderEmail);

            var projectIds = new List<int>();
            await using (var reader = await localProjectsCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    projectIds.Add(reader.GetInt32(0));
            }

            foreach (var projectId in projectIds)
            {
                var localStateCmd = connection.CreateCommand();
                localStateCmd.CommandText = @"
SELECT IsOwner, IsPending
FROM ProjectMember
WHERE ProjectID = @pid AND UserEmail = @local COLLATE NOCASE
LIMIT 1";
                localStateCmd.Parameters.AddWithValue("@pid", projectId);
                localStateCmd.Parameters.AddWithValue("@local", LocalOwnerPlaceholderEmail);

                var localIsOwner = false;
                var localIsPending = false;
                await using (var localReader = await localStateCmd.ExecuteReaderAsync())
                {
                    if (!await localReader.ReadAsync())
                        continue;

                    localIsOwner = localReader.GetBoolean(0);
                    localIsPending = localReader.GetBoolean(1);
                }

                var currentExistsCmd = connection.CreateCommand();
                currentExistsCmd.CommandText = @"
SELECT 1
FROM ProjectMember
WHERE ProjectID = @pid AND UserEmail = @email COLLATE NOCASE
LIMIT 1";
                currentExistsCmd.Parameters.AddWithValue("@pid", projectId);
                currentExistsCmd.Parameters.AddWithValue("@email", normalizedCurrentEmail);

                var currentExists = await currentExistsCmd.ExecuteScalarAsync() is not null;
                if (currentExists)
                {
                    var promoteCurrentCmd = connection.CreateCommand();
                    promoteCurrentCmd.CommandText = @"
UPDATE ProjectMember
SET IsOwner = CASE WHEN @owner = 1 THEN 1 ELSE IsOwner END,
    IsPending = 0
WHERE ProjectID = @pid AND UserEmail = @email COLLATE NOCASE";
                    promoteCurrentCmd.Parameters.AddWithValue("@owner", localIsOwner ? 1 : 0);
                    promoteCurrentCmd.Parameters.AddWithValue("@pid", projectId);
                    promoteCurrentCmd.Parameters.AddWithValue("@email", normalizedCurrentEmail);
                    await promoteCurrentCmd.ExecuteNonQueryAsync();

                    var deleteLocalCmd = connection.CreateCommand();
                    deleteLocalCmd.CommandText =
                        "DELETE FROM ProjectMember WHERE ProjectID = @pid AND UserEmail = @local COLLATE NOCASE";
                    deleteLocalCmd.Parameters.AddWithValue("@pid", projectId);
                    deleteLocalCmd.Parameters.AddWithValue("@local", LocalOwnerPlaceholderEmail);
                    await deleteLocalCmd.ExecuteNonQueryAsync();
                    continue;
                }

                var moveLocalCmd = connection.CreateCommand();
                moveLocalCmd.CommandText = @"
UPDATE ProjectMember
SET UserEmail = @email,
    IsPending = 0,
    IsOwner = CASE WHEN @owner = 1 THEN 1 ELSE IsOwner END
WHERE ProjectID = @pid AND UserEmail = @local COLLATE NOCASE";
                moveLocalCmd.Parameters.AddWithValue("@email", normalizedCurrentEmail);
                moveLocalCmd.Parameters.AddWithValue("@owner", localIsOwner ? 1 : 0);
                moveLocalCmd.Parameters.AddWithValue("@pid", projectId);
                moveLocalCmd.Parameters.AddWithValue("@local", LocalOwnerPlaceholderEmail);
                await moveLocalCmd.ExecuteNonQueryAsync();

                if (localIsPending)
                {
                    var ensureActiveCmd = connection.CreateCommand();
                    ensureActiveCmd.CommandText = @"
UPDATE ProjectMember
SET IsPending = 0
WHERE ProjectID = @pid AND UserEmail = @email COLLATE NOCASE";
                    ensureActiveCmd.Parameters.AddWithValue("@pid", projectId);
                    ensureActiveCmd.Parameters.AddWithValue("@email", normalizedCurrentEmail);
                    await ensureActiveCmd.ExecuteNonQueryAsync();
                }
            }
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
                ? (await _authService.GetEmailAsync() is { } u ? NormalizeEmail(u) : LocalOwnerPlaceholderEmail)
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

            var current = await _authService.GetEmailAsync() is { } c ? NormalizeEmail(c) : null;
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
                return members.Any(m => m.IsOwner && m.UserEmail.Equals(LocalOwnerPlaceholderEmail, StringComparison.OrdinalIgnoreCase));

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
