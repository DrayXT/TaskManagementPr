using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using TaskManagementPr.Models;

namespace TaskManagementPr.Data
{
    public class TaskRepository
    {
        private bool _hasBeenInitialized;
        private readonly ILogger _logger;

        public TaskRepository(ILogger<TaskRepository> logger)
        {
            _logger = logger;
        }

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
CREATE TABLE IF NOT EXISTS Task (
    ID INTEGER PRIMARY KEY AUTOINCREMENT,
    Title TEXT NOT NULL,
    IsCompleted INTEGER NOT NULL,
    ProjectID INTEGER NOT NULL
);";
                await createTableCmd.ExecuteNonQueryAsync();

                await MigrateTaskColumnsAsync(connection);

                createTableCmd.CommandText = @"
CREATE TABLE IF NOT EXISTS TaskAssignee (
    TaskID INTEGER NOT NULL,
    UserEmail TEXT NOT NULL COLLATE NOCASE,
    PointsAllocated INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (TaskID, UserEmail)
);";
                await createTableCmd.ExecuteNonQueryAsync();

                await MigrateTaskAssigneeColumnsAsync(connection);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error creating Task table");
                throw;
            }

            _hasBeenInitialized = true;
        }

        private static async Task MigrateTaskAssigneeColumnsAsync(SqliteConnection connection)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA table_info(TaskAssignee)";
            await using (var r = await pragma.ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                    columns.Add(r.GetString(1));
            }

            if (!columns.Contains("PointsAllocated"))
            {
                var c = connection.CreateCommand();
                c.CommandText = "ALTER TABLE TaskAssignee ADD COLUMN PointsAllocated INTEGER NOT NULL DEFAULT 0";
                await c.ExecuteNonQueryAsync();
            }
        }

        private static async Task MigrateTaskColumnsAsync(SqliteConnection connection)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA table_info(Task)";
            await using (var r = await pragma.ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                    columns.Add(r.GetString(1));
            }

            async Task AddColumn(string sql)
            {
                var c = connection.CreateCommand();
                c.CommandText = sql;
                await c.ExecuteNonQueryAsync();
            }

            if (!columns.Contains("Priority"))
                await AddColumn("ALTER TABLE Task ADD COLUMN Priority INTEGER NOT NULL DEFAULT 1");

            if (!columns.Contains("RewardPoints"))
                await AddColumn("ALTER TABLE Task ADD COLUMN RewardPoints INTEGER NOT NULL DEFAULT 100");

            if (!columns.Contains("DueDate"))
                await AddColumn("ALTER TABLE Task ADD COLUMN DueDate TEXT NULL");
        }

        private static ProjectTask ReadTask(SqliteDataReader reader)
        {
            static int Ord(SqliteDataReader dr, string name)
            {
                for (var i = 0; i < dr.FieldCount; i++)
                {
                    if (string.Equals(dr.GetName(i), name, StringComparison.OrdinalIgnoreCase))
                        return i;
                }

                return -1;
            }

            var idOrd = Ord(reader, "ID");
            var titleOrd = Ord(reader, "Title");
            var completedOrd = Ord(reader, "IsCompleted");
            var projectOrd = Ord(reader, "ProjectID");
            var priorityOrd = Ord(reader, "Priority");
            var rewardOrd = Ord(reader, "RewardPoints");
            var dueOrd = Ord(reader, "DueDate");

            DateTime? due = null;
            if (dueOrd >= 0 && !reader.IsDBNull(dueOrd))
            {
                var s = reader.GetString(dueOrd);
                if (DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                    due = parsed;
            }

            return new ProjectTask
            {
                ID = reader.GetInt32(idOrd),
                Title = reader.GetString(titleOrd),
                IsCompleted = reader.GetBoolean(completedOrd),
                ProjectID = reader.GetInt32(projectOrd),
                Priority = priorityOrd >= 0 && !reader.IsDBNull(priorityOrd)
                    ? (TaskPriority)reader.GetInt32(priorityOrd)
                    : TaskPriority.Normal,
                RewardPoints = rewardOrd >= 0 && !reader.IsDBNull(rewardOrd) ? reader.GetInt32(rewardOrd) : 100,
                DueDate = due
            };
        }

        private static async Task<Dictionary<int, List<(string Email, int Points)>>> LoadAssigneesDetailAsync(
            SqliteConnection connection,
            IReadOnlyCollection<int> taskIds)
        {
            var map = new Dictionary<int, List<(string Email, int Points)>>();
            if (taskIds.Count == 0)
                return map;

            var cmd = connection.CreateCommand();
            var paramNames = new List<string>();
            var i = 0;
            foreach (var id in taskIds)
            {
                var pn = "@t" + i++;
                paramNames.Add(pn);
                cmd.Parameters.AddWithValue(pn, id);
            }

            cmd.CommandText =
                $"SELECT TaskID, UserEmail, PointsAllocated FROM TaskAssignee WHERE TaskID IN ({string.Join(",", paramNames)})";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var taskId = reader.GetInt32(0);
                var email = reader.GetString(1);
                var pts = reader.FieldCount > 2 && !reader.IsDBNull(2) ? reader.GetInt32(2) : 0;
                if (!map.TryGetValue(taskId, out var list))
                {
                    list = [];
                    map[taskId] = list;
                }

                list.Add((email.ToLowerInvariant(), pts));
            }

            return map;
        }

        private static void ApplyAssigneeHydration(ProjectTask task, List<(string Email, int Points)>? rows)
        {
            task.AssigneeEmails.Clear();
            task.AssigneePointShares.Clear();
            if (rows is null || rows.Count == 0)
                return;

            foreach (var (email, pts) in rows)
            {
                task.AssigneeEmails.Add(email);
                task.AssigneePointShares[email] = pts;
            }
        }

        private static async Task HydrateAssigneesAsync(SqliteConnection connection, List<ProjectTask> tasks)
        {
            var ids = tasks.Where(t => t.ID > 0).Select(t => t.ID).ToList();
            var map = await LoadAssigneesDetailAsync(connection, ids);
            foreach (var t in tasks)
            {
                if (map.TryGetValue(t.ID, out var rows))
                    ApplyAssigneeHydration(t, rows);
            }
        }

        public async Task<List<ProjectTask>> ListAsync()
        {
            await Init();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var selectCmd = connection.CreateCommand();
            selectCmd.CommandText = "SELECT * FROM Task";
            var tasks = new List<ProjectTask>();

            await using (var reader = await selectCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    tasks.Add(ReadTask(reader));
            }

            await HydrateAssigneesAsync(connection, tasks);
            return tasks;
        }

        public async Task<List<ProjectTask>> ListAsync(int projectId)
        {
            await Init();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var selectCmd = connection.CreateCommand();
            selectCmd.CommandText = "SELECT * FROM Task WHERE ProjectID = @projectId";
            selectCmd.Parameters.AddWithValue("@projectId", projectId);
            var tasks = new List<ProjectTask>();

            await using (var reader = await selectCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    tasks.Add(ReadTask(reader));
            }

            await HydrateAssigneesAsync(connection, tasks);
            return tasks;
        }

        public async Task<ProjectTask?> GetAsync(int id)
        {
            await Init();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var selectCmd = connection.CreateCommand();
            selectCmd.CommandText = "SELECT * FROM Task WHERE ID = @id";
            selectCmd.Parameters.AddWithValue("@id", id);

            await using var reader = await selectCmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var task = ReadTask(reader);
            var map = await LoadAssigneesDetailAsync(connection, [task.ID]);
            if (map.TryGetValue(task.ID, out var rows))
                ApplyAssigneeHydration(task, rows);
            return task;
        }

        public async Task<int> SaveItemAsync(ProjectTask item)
        {
            await Init();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var saveCmd = connection.CreateCommand();
            if (item.ID == 0)
            {
                saveCmd.CommandText = @"
INSERT INTO Task (Title, IsCompleted, ProjectID, Priority, RewardPoints, DueDate)
VALUES (@title, @isCompleted, @projectId, @priority, @reward, @due);
SELECT last_insert_rowid();";
            }
            else
            {
                saveCmd.CommandText = @"
UPDATE Task SET Title = @title, IsCompleted = @isCompleted, ProjectID = @projectId,
    Priority = @priority, RewardPoints = @reward, DueDate = @due
WHERE ID = @id";
                saveCmd.Parameters.AddWithValue("@id", item.ID);
            }

            saveCmd.Parameters.AddWithValue("@title", item.Title);
            saveCmd.Parameters.AddWithValue("@isCompleted", item.IsCompleted);
            saveCmd.Parameters.AddWithValue("@projectId", item.ProjectID);
            saveCmd.Parameters.AddWithValue("@priority", (int)item.Priority);
            saveCmd.Parameters.AddWithValue("@reward", item.RewardPoints);
            saveCmd.Parameters.AddWithValue("@due", item.DueDate.HasValue
                ? item.DueDate.Value.ToString("o")
                : (object)DBNull.Value);

            var result = await saveCmd.ExecuteScalarAsync();
            if (item.ID == 0)
                item.ID = Convert.ToInt32(result);

            await ReplaceAssigneesAsync(connection, item);
            return item.ID;
        }

        private static async Task ReplaceAssigneesAsync(SqliteConnection connection, ProjectTask item)
        {
            var del = connection.CreateCommand();
            del.CommandText = "DELETE FROM TaskAssignee WHERE TaskID = @id";
            del.Parameters.AddWithValue("@id", item.ID);
            await del.ExecuteNonQueryAsync();

            foreach (var email in item.AssigneeEmails.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var e = email.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(e))
                    continue;

                var pts = 0;
                if (!item.AssigneePointShares.TryGetValue(e, out pts))
                {
                    foreach (var kv in item.AssigneePointShares)
                    {
                        if (!kv.Key.Equals(e, StringComparison.OrdinalIgnoreCase))
                            continue;
                        pts = kv.Value;
                        break;
                    }
                }

                var ins = connection.CreateCommand();
                ins.CommandText =
                    "INSERT INTO TaskAssignee (TaskID, UserEmail, PointsAllocated) VALUES (@tid, @email, @pts)";
                ins.Parameters.AddWithValue("@tid", item.ID);
                ins.Parameters.AddWithValue("@email", e);
                ins.Parameters.AddWithValue("@pts", Math.Max(0, pts));
                await ins.ExecuteNonQueryAsync();
            }
        }

        public async Task<int> DeleteItemAsync(ProjectTask item)
        {
            await Init();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var delA = connection.CreateCommand();
            delA.CommandText = "DELETE FROM TaskAssignee WHERE TaskID = @id";
            delA.Parameters.AddWithValue("@id", item.ID);
            await delA.ExecuteNonQueryAsync();

            var deleteCmd = connection.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM Task WHERE ID = @id";
            deleteCmd.Parameters.AddWithValue("@id", item.ID);

            return await deleteCmd.ExecuteNonQueryAsync();
        }

        public async Task DeleteAllForProjectAsync(int projectId)
        {
            await Init();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var idsCmd = connection.CreateCommand();
            idsCmd.CommandText = "SELECT ID FROM Task WHERE ProjectID = @pid";
            idsCmd.Parameters.AddWithValue("@pid", projectId);
            var ids = new List<int>();
            await using (var r = await idsCmd.ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                    ids.Add(r.GetInt32(0));
            }

            foreach (var id in ids)
            {
                var delA = connection.CreateCommand();
                delA.CommandText = "DELETE FROM TaskAssignee WHERE TaskID = @id";
                delA.Parameters.AddWithValue("@id", id);
                await delA.ExecuteNonQueryAsync();
            }

            var delT = connection.CreateCommand();
            delT.CommandText = "DELETE FROM Task WHERE ProjectID = @pid";
            delT.Parameters.AddWithValue("@pid", projectId);
            await delT.ExecuteNonQueryAsync();
        }

        public async Task DropTableAsync()
        {
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var dropA = connection.CreateCommand();
            dropA.CommandText = "DROP TABLE IF EXISTS TaskAssignee";
            await dropA.ExecuteNonQueryAsync();

            var dropTableCmd = connection.CreateCommand();
            dropTableCmd.CommandText = "DROP TABLE IF EXISTS Task";
            await dropTableCmd.ExecuteNonQueryAsync();
            _hasBeenInitialized = false;
        }
    }
}
