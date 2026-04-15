using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using TaskManagementPr.Models;

namespace TaskManagementPr.Data
{
    public class ProjectRepository
    {
        private bool _hasBeenInitialized;
        private readonly ILogger _logger;
        private readonly TaskRepository _taskRepository;
        private readonly TagRepository _tagRepository;
        private readonly ProjectMemberRepository _projectMemberRepository;

        public ProjectRepository(
            TaskRepository taskRepository,
            TagRepository tagRepository,
            ProjectMemberRepository projectMemberRepository,
            ILogger<ProjectRepository> logger)
        {
            _taskRepository = taskRepository;
            _tagRepository = tagRepository;
            _projectMemberRepository = projectMemberRepository;
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
CREATE TABLE IF NOT EXISTS Project (
    ID INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    Description TEXT NOT NULL,
    Icon TEXT NOT NULL,
    CategoryID INTEGER NOT NULL
);";
                await createTableCmd.ExecuteNonQueryAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error creating Project table");
                throw;
            }

            _hasBeenInitialized = true;
        }

        private async Task HydrateProjectAsync(Project project)
        {
            project.Tags = await _tagRepository.ListAsync(project.ID);
            project.Tasks = await _taskRepository.ListAsync(project.ID);
            project.Members = await _projectMemberRepository.ListByProjectAsync(project.ID);
        }

        public async Task<List<Project>> ListAsync()
        {
            await Init();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var selectCmd = connection.CreateCommand();
            selectCmd.CommandText = "SELECT * FROM Project";
            var projects = new List<Project>();

            await using var reader = await selectCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                projects.Add(new Project
                {
                    ID = reader.GetInt32(0),
                    Name = reader.GetString(1),
                    Description = reader.GetString(2),
                    Icon = reader.GetString(3),
                    CategoryID = reader.GetInt32(4)
                });
            }

            foreach (var project in projects)
                await HydrateProjectAsync(project);

            return projects;
        }

        public async Task<Project?> GetAsync(int id)
        {
            await Init();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var selectCmd = connection.CreateCommand();
            selectCmd.CommandText = "SELECT * FROM Project WHERE ID = @id";
            selectCmd.Parameters.AddWithValue("@id", id);

            await using var reader = await selectCmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
                return null;

            var project = new Project
            {
                ID = reader.GetInt32(0),
                Name = reader.GetString(1),
                Description = reader.GetString(2),
                Icon = reader.GetString(3),
                CategoryID = reader.GetInt32(4)
            };

            await HydrateProjectAsync(project);
            return project;
        }

        public async Task<int> SaveItemAsync(Project item)
        {
            await Init();
            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var saveCmd = connection.CreateCommand();
            if (item.ID == 0)
            {
                saveCmd.CommandText = @"
INSERT INTO Project (Name, Description, Icon, CategoryID)
VALUES (@Name, @Description, @Icon, @CategoryID);
SELECT last_insert_rowid();";
            }
            else
            {
                saveCmd.CommandText = @"
UPDATE Project
SET Name = @Name, Description = @Description, Icon = @Icon, CategoryID = @CategoryID
WHERE ID = @ID";
                saveCmd.Parameters.AddWithValue("@ID", item.ID);
            }

            saveCmd.Parameters.AddWithValue("@Name", item.Name);
            saveCmd.Parameters.AddWithValue("@Description", item.Description);
            saveCmd.Parameters.AddWithValue("@Icon", item.Icon);
            saveCmd.Parameters.AddWithValue("@CategoryID", item.CategoryID);

            var result = await saveCmd.ExecuteScalarAsync();
            if (item.ID == 0)
                item.ID = Convert.ToInt32(result);

            return item.ID;
        }

        public async Task<int> DeleteItemAsync(Project item)
        {
            await Init();
            await _taskRepository.DeleteAllForProjectAsync(item.ID);
            await _projectMemberRepository.DeleteAllForProjectAsync(item.ID);

            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var delTags = connection.CreateCommand();
            delTags.CommandText = "DELETE FROM ProjectsTags WHERE ProjectID = @ID";
            delTags.Parameters.AddWithValue("@ID", item.ID);
            await delTags.ExecuteNonQueryAsync();

            var deleteCmd = connection.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM Project WHERE ID = @ID";
            deleteCmd.Parameters.AddWithValue("@ID", item.ID);

            return await deleteCmd.ExecuteNonQueryAsync();
        }

        public async Task DropTableAsync()
        {
            await Init();

            await _projectMemberRepository.DropTableAsync();
            await _tagRepository.DropTableAsync();
            await _taskRepository.DropTableAsync();

            await using var connection = new SqliteConnection(Constants.DatabasePath);
            await connection.OpenAsync();

            var dropCmd = connection.CreateCommand();
            dropCmd.CommandText = "DROP TABLE IF EXISTS Project";
            await dropCmd.ExecuteNonQueryAsync();
            _hasBeenInitialized = false;
        }
    }
}
