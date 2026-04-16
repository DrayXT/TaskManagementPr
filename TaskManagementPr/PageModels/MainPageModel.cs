using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using TaskManagementPr.Data;
using TaskManagementPr.Messaging;
using TaskManagementPr.Models;
using TaskManagementPr.Services;
using TaskManagementPr.Utilities;

namespace TaskManagementPr.PageModels
{
    public partial class MainPageModel : ObservableObject, IProjectTaskPageModel
    {
        private bool _isNavigatedTo;
        private bool _dataLoaded;
        private readonly ProjectRepository _projectRepository;
        private readonly TaskRepository _taskRepository;
        private readonly CategoryRepository _categoryRepository;
        private readonly ProjectMemberRepository _projectMemberRepository;
        private readonly IAuthService _authService;
        private readonly ModalErrorHandler _errorHandler;
        private readonly SeedDataService _seedDataService;

        [ObservableProperty]
        private List<CategoryChartData> _todoCategoryData = [];

        [ObservableProperty]
        private List<Brush> _todoCategoryColors = [];

        [ObservableProperty]
        private List<ProjectTask> _tasks = [];

        [ObservableProperty]
        private List<Project> _projects = [];

        [ObservableProperty]
        bool _isBusy;

        [ObservableProperty]
        bool _isRefreshing;

        [ObservableProperty]
        private string _today = DateTime.Now.ToString("dddd, MMM d");

        [ObservableProperty]
        private Project? selectedProject;

        public bool HasCompletedTasks
            => Tasks?.Any(t => t.IsCompleted) ?? false;

        public MainPageModel(
            SeedDataService seedDataService,
            ProjectRepository projectRepository,
            TaskRepository taskRepository,
            CategoryRepository categoryRepository,
            ProjectMemberRepository projectMemberRepository,
            IAuthService authService,
            ModalErrorHandler errorHandler)
        {
            _projectRepository = projectRepository;
            _taskRepository = taskRepository;
            _categoryRepository = categoryRepository;
            _projectMemberRepository = projectMemberRepository;
            _authService = authService;
            _errorHandler = errorHandler;
            _seedDataService = seedDataService;
        }

        private async Task<bool> CanManageTaskAsync(ProjectTask task)
        {
            if (task.ProjectID <= 0)
                return true;

            var project = await _projectRepository.GetAsync(task.ProjectID);
            return project is not null && _projectMemberRepository.IsCurrentUserOwner(project.ID, project.Members);
        }

        private async Task LoadData()
        {
            try
            {
                IsBusy = true;

                var realEmail = await _authService.GetEmailAsync();
                await _projectMemberRepository.ActivatePendingForEmailAsync(realEmail);
                var me = ProjectVisibilityRules.Normalize(realEmail) ?? ProjectVisibilityRules.LocalOwnerPlaceholderEmail;

                var allProjects = await _projectRepository.ListAsync();
                Projects = allProjects
                    .Where(project => ProjectVisibilityRules.ShouldIncludeProject(
                        me,
                        project.Members.Where(m => !m.IsPending).ToList(),
                        project.Tasks))
                    .ToList();

                var chartData = new List<CategoryChartData>();
                var chartColors = new List<Brush>();

                var categories = await _categoryRepository.ListAsync();
                foreach (var category in categories)
                {
                    chartColors.Add(category.ColorBrush);

                    var ps = Projects.Where(p => p.CategoryID == category.ID).ToList();
                    int tasksCount = ps.SelectMany(p => p.Tasks).Count();

                    chartData.Add(new(category.Title, tasksCount));
                }

                TodoCategoryData = chartData;
                TodoCategoryColors = chartColors;

                var visibleProjectIds = Projects.Select(p => p.ID).ToHashSet();
                Tasks = (await _taskRepository.ListAsync())
                    .Where(task => ProjectVisibilityRules.ShouldIncludeTask(me, task, visibleProjectIds))
                    .ToList();
            }
            finally
            {
                IsBusy = false;
                OnPropertyChanged(nameof(HasCompletedTasks));
            }
        }

        private async Task InitData(SeedDataService seedDataService)
        {
            bool isSeeded = Preferences.Default.ContainsKey("is_seeded");

            if (!isSeeded)
            {
                await seedDataService.LoadSeedDataAsync();
            }

            Preferences.Default.Set("is_seeded", true);
            await Refresh();
        }

        [RelayCommand]
        private async Task Refresh()
        {
            try
            {
                IsRefreshing = true;
                await LoadData();
            }
            catch (Exception e)
            {
                _errorHandler.HandleError(e);
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        [RelayCommand]
        private void NavigatedTo() =>
            _isNavigatedTo = true;

        [RelayCommand]
        private void NavigatedFrom() =>
            _isNavigatedTo = false;

        [RelayCommand]
        private async Task Appearing()
        {
            if (!_dataLoaded)
            {
                await InitData(_seedDataService);
                _dataLoaded = true;
                await Refresh();
            }
            // This means we are being navigated to
            else if (!_isNavigatedTo)
            {
                await Refresh();
            }
        }

        [RelayCommand]
        private async Task TaskCompleted(ProjectTask task)
        {
            if (!await CanManageTaskAsync(task))
            {
                task.IsCompleted = !task.IsCompleted;
                _errorHandler.HandleError(new Exception("Только владелец проекта может менять задачи этого проекта."));
                await LoadData();
                return;
            }

            if (task.IsCompleted)
                TaskStatisticsPoints.ApplyCompletionDefaults(task);

            await _taskRepository.SaveItemAsync(task);
            WeakReferenceMessenger.Default.Send(new TaskDataChangedMessage());
            await LoadData();
        }

        [RelayCommand]
        private async Task DeleteTask(ProjectTask task)
        {
            var page = Shell.Current?.CurrentPage;
            if (task is null || page is null)
                return;

            if (!await CanManageTaskAsync(task))
            {
                _errorHandler.HandleError(new Exception("Только владелец проекта может удалять задачи этого проекта."));
                await LoadData();
                return;
            }

            var ok = await page.DisplayAlertAsync(
                "Удалить задачу",
                $"Удалить задачу \"{task.Title}\"?",
                "Да",
                "Нет");
            if (!ok)
                return;

            await _taskRepository.DeleteItemAsync(task);
            WeakReferenceMessenger.Default.Send(new TaskDataChangedMessage());
            await LoadData();
        }

        [RelayCommand]
        private Task AddTask()
            => Shell.Current.GoToAsync($"task");

        [RelayCommand]
        private Task? NavigateToProject(Project project)
            => project is null ? null : Shell.Current.GoToAsync($"project?id={project.ID}");

        [RelayCommand]
        private Task NavigateToTask(ProjectTask task)
            => Shell.Current.GoToAsync($"task?id={task.ID}");

        [RelayCommand]
        private async Task CleanTasks()
        {
            var completedTasks = Tasks.Where(t => t.IsCompleted).ToList();
            var blocked = false;
            foreach (var task in completedTasks)
            {
                if (!await CanManageTaskAsync(task))
                {
                    blocked = true;
                    continue;
                }

                await _taskRepository.DeleteItemAsync(task);
                Tasks.Remove(task);
            }

            WeakReferenceMessenger.Default.Send(new TaskDataChangedMessage());
            await LoadData();
            if (blocked)
                _errorHandler.HandleError(new Exception("Часть завершённых задач не была удалена: ими может управлять только владелец проекта."));
            await AppShell.DisplayToastAsync("Завершённые задачи удалены");
        }
    }
}