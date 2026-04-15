using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using TaskManagementPr.Data;
using TaskManagementPr.Messaging;
using TaskManagementPr.Models;
using TaskManagementPr.Services;

namespace TaskManagementPr.PageModels
{
    public partial class ProjectDetailPageModel : ObservableObject, IQueryAttributable, IProjectTaskPageModel
    {
        private Project? _project;
        private readonly ProjectRepository _projectRepository;
        private readonly TaskRepository _taskRepository;
        private readonly CategoryRepository _categoryRepository;
        private readonly ProjectMemberRepository _projectMemberRepository;
        private readonly IAuthService _authService;
        private readonly ModalErrorHandler _errorHandler;

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _description = string.Empty;

        [ObservableProperty]
        private List<ProjectTask> _tasks = [];

        [ObservableProperty]
        private ObservableCollection<ProjectMember> _members = [];

        [ObservableProperty]
        private bool _isCurrentUserOwner;

        [ObservableProperty]
        private bool _hasPersistedProject;

        [ObservableProperty]
        private bool _canManageMembers;

        [ObservableProperty]
        private List<Category> _categories = [];

        [ObservableProperty]
        private Category? _category;

        [ObservableProperty]
        private int _categoryIndex = -1;

        [ObservableProperty]
        private IconData _icon;

        [ObservableProperty]
        bool _isBusy;

        [ObservableProperty]
        private List<IconData> _icons = new List<IconData>
        {
            new IconData { Icon = FluentUI.ribbon_24_regular, Description = "Ribbon Icon" },
            new IconData { Icon = FluentUI.ribbon_star_24_regular, Description = "Ribbon Star Icon" },
            new IconData { Icon = FluentUI.trophy_24_regular, Description = "Trophy Icon" },
            new IconData { Icon = FluentUI.badge_24_regular, Description = "Badge Icon" },
            new IconData { Icon = FluentUI.book_24_regular, Description = "Book Icon" },
            new IconData { Icon = FluentUI.people_24_regular, Description = "People Icon" },
            new IconData { Icon = FluentUI.bot_24_regular, Description = "Bot Icon" }
        };

        private bool _canDelete;

        public bool CanDelete
        {
            get => _canDelete;
            set
            {
                _canDelete = value;
                DeleteCommand.NotifyCanExecuteChanged();
            }
        }

        public bool HasCompletedTasks
            => _project?.Tasks.Any(t => t.IsCompleted) ?? false;

        public ProjectDetailPageModel(
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
            _icon = _icons.First();
            Tasks = [];
        }

        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            if (query.ContainsKey("id"))
            {
                int id = Convert.ToInt32(query["id"]);
                LoadData(id).FireAndForgetSafeAsync(_errorHandler);
            }
            else if (query.ContainsKey("refresh"))
            {
                RefreshData().FireAndForgetSafeAsync(_errorHandler);
            }
            else
            {
                LoadCategories().FireAndForgetSafeAsync(_errorHandler);
                _project = new();
                _project.Tasks = [];
                Tasks = _project.Tasks;
                Members = [];
                IsCurrentUserOwner = true;
                HasPersistedProject = false;
                CanManageMembers = false;
            }
        }

        private async Task LoadCategories() =>
            Categories = await _categoryRepository.ListAsync();

        private void SyncMemberUi()
        {
            if (_project is null)
                return;

            var list = _project.Members.ToList();
            Members = new ObservableCollection<ProjectMember>(list);
            IsCurrentUserOwner = _projectMemberRepository.IsCurrentUserOwner(_project.ID, list);
            HasPersistedProject = _project.ID > 0;
            CanManageMembers = IsCurrentUserOwner && HasPersistedProject;
            CanDelete = !_project.IsNullOrNew() && IsCurrentUserOwner;
        }

        private async Task RefreshData()
        {
            if (_project.IsNullOrNew())
            {
                if (_project is not null)
                    Tasks = new(_project.Tasks);

                return;
            }

            Tasks = await _taskRepository.ListAsync(_project.ID);
            _project.Tasks = Tasks;
            _project.Members = await _projectMemberRepository.ListByProjectAsync(_project.ID);
            SyncMemberUi();
        }

        private async Task LoadData(int id)
        {
            try
            {
                IsBusy = true;

                _project = await _projectRepository.GetAsync(id);

                if (_project.IsNullOrNew())
                {
                    _errorHandler.HandleError(new Exception($"Project with id {id} could not be found."));
                    return;
                }

                Name = _project.Name;
                Description = _project.Description;
                Tasks = _project.Tasks;
                SyncMemberUi();

                foreach (var icon in Icons)
                {
                    if (icon.Icon == _project.Icon)
                    {
                        Icon = icon;
                        break;
                    }
                }

                Categories = await _categoryRepository.ListAsync();
                Category = Categories?.FirstOrDefault(c => c.ID == _project.CategoryID);
                CategoryIndex = Categories?.FindIndex(c => c.ID == _project.CategoryID) ?? -1;
            }
            catch (Exception e)
            {
                _errorHandler.HandleError(e);
            }
            finally
            {
                IsBusy = false;
                OnPropertyChanged(nameof(HasCompletedTasks));
            }
        }

        [RelayCommand]
        private async Task TaskCompleted(ProjectTask task)
        {
            await _taskRepository.SaveItemAsync(task);
            WeakReferenceMessenger.Default.Send(new TaskDataChangedMessage());
            OnPropertyChanged(nameof(HasCompletedTasks));
            if (_project is not null && !_project.IsNullOrNew())
            {
                Tasks = await _taskRepository.ListAsync(_project.ID);
                _project.Tasks = Tasks;
            }
        }

        [RelayCommand]
        private async Task Save()
        {
            if (_project is null)
            {
                _errorHandler.HandleError(
                    new Exception("Project is null. Cannot Save."));

                return;
            }

            _project.Name = Name;
            _project.Description = Description;
            _project.CategoryID = Category?.ID ?? 0;
            _project.Icon = Icon.Icon ?? FluentUI.ribbon_24_regular;
            await _projectRepository.SaveItemAsync(_project);
            await _projectMemberRepository.EnsureOwnerAsync(_project.ID, _authService.CurrentUserEmail);

            foreach (var task in _project.Tasks)
            {
                if (task.ID == 0)
                {
                    task.ProjectID = _project.ID;
                    await _taskRepository.SaveItemAsync(task);
                }
            }

            WeakReferenceMessenger.Default.Send(new TaskDataChangedMessage());
            _project.Members = await _projectMemberRepository.ListByProjectAsync(_project.ID);
            SyncMemberUi();

            await Shell.Current.GoToAsync("..");
            await AppShell.DisplayToastAsync("Проект сохранён");
        }

        [RelayCommand]
        private async Task AddTask()
        {
            if (_project is null)
            {
                _errorHandler.HandleError(
                    new Exception("Project is null. Cannot navigate to task."));

                return;
            }

            await Shell.Current.GoToAsync($"task",
                new ShellNavigationQueryParameters(){
                    {TaskDetailPageModel.ProjectQueryKey, _project}
                });
        }

        [RelayCommand(CanExecute = nameof(CanDelete))]
        private async Task Delete()
        {
            if (_project.IsNullOrNew())
            {
                await Shell.Current.GoToAsync("..");
                return;
            }

            if (!IsCurrentUserOwner)
            {
                _errorHandler.HandleError(new Exception("Удалить проект может только владелец."));
                return;
            }

            await _projectRepository.DeleteItemAsync(_project);
            await Shell.Current.GoToAsync("..");
            await AppShell.DisplayToastAsync("Проект удалён");
        }

        [RelayCommand]
        private Task NavigateToTask(ProjectTask task) =>
            Shell.Current.GoToAsync($"task?id={task.ID}");

        [RelayCommand]
        private void IconSelected(IconData icon)
        {
            SemanticScreenReader.Announce($"{icon.Description} selected");
        }

        [RelayCommand]
        private async Task CleanTasks()
        {
            var completedTasks = Tasks.Where(t => t.IsCompleted).ToArray();
            foreach (var task in completedTasks)
            {
                await _taskRepository.DeleteItemAsync(task);
                Tasks.Remove(task);
            }

            Tasks = new(Tasks);
            OnPropertyChanged(nameof(HasCompletedTasks));
            await AppShell.DisplayToastAsync("Завершённые задачи удалены");
        }

        [RelayCommand]
        private async Task InviteMember()
        {
            if (_project is null || _project.ID == 0 || !IsCurrentUserOwner)
                return;

            var page = Shell.Current?.CurrentPage;
            if (page is null)
                return;

            var email = await page.DisplayPromptAsync("Приглашение", "Введите email участника:", "Добавить", "Отмена");
            if (string.IsNullOrWhiteSpace(email))
                return;

            try
            {
                await _projectMemberRepository.InviteOrAddMemberAsync(_project.ID, email.Trim());
                _project.Members = await _projectMemberRepository.ListByProjectAsync(_project.ID);
                SyncMemberUi();
                await AppShell.DisplayToastAsync("Участник добавлен или приглашён");
            }
            catch (Exception ex)
            {
                _errorHandler.HandleError(ex);
            }
        }

        [RelayCommand]
        private async Task RemoveMember(ProjectMember? member)
        {
            if (_project is null || member is null || !IsCurrentUserOwner)
                return;

            var page = Shell.Current?.CurrentPage;
            if (page is null)
                return;

            if (member.IsOwner && Members.Count(m => m.IsOwner && !m.IsPending) <= 1)
            {
                await page.DisplayAlertAsync("Нельзя удалить", "Сначала передайте права владельца другому участнику.", "OK");
                return;
            }

            var ok = await page.DisplayAlertAsync("Удалить участника", $"Убрать {member.UserEmail} из проекта?", "Да", "Нет");
            if (!ok)
                return;

            await _projectMemberRepository.RemoveMemberAsync(member);
            _project.Members = await _projectMemberRepository.ListByProjectAsync(_project.ID);
            SyncMemberUi();
        }

        [RelayCommand]
        private async Task TransferOwnership()
        {
            if (_project is null || _project.ID == 0 || !IsCurrentUserOwner)
                return;

            var page = Shell.Current?.CurrentPage;
            if (page is null)
                return;

            var candidates = Members
                .Where(m => !m.IsPending && !m.IsOwner)
                .Select(m => m.UserEmail)
                .ToArray();

            if (candidates.Length == 0)
            {
                await page.DisplayAlertAsync("Нет кандидатов", "Добавьте активного участника, которому можно передать права.", "OK");
                return;
            }

            var picked = await page.DisplayActionSheetAsync("Новый владелец", "Отмена", null, candidates);
            if (picked is null || picked == "Отмена" || string.IsNullOrEmpty(picked))
                return;

            await _projectMemberRepository.TransferOwnershipAsync(_project.ID, picked);
            _project.Members = await _projectMemberRepository.ListByProjectAsync(_project.ID);
            SyncMemberUi();
            await AppShell.DisplayToastAsync("Владелец изменён");
        }

        [RelayCommand]
        private async Task LeaveProject()
        {
            if (_project is null || _project.ID == 0)
                return;

            var page = Shell.Current?.CurrentPage;
            if (page is null)
                return;

            var me = _authService.CurrentUserEmail?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(me))
                me = "local@owner.app";

            var self = Members.FirstOrDefault(m =>
                m.UserEmail.Equals(me, StringComparison.OrdinalIgnoreCase) && !m.IsPending);

            if (self is null)
            {
                await page.DisplayAlertAsync("Проект", "Вы не состоите в этом проекте.", "OK");
                return;
            }

            if (self.IsOwner)
            {
                var owners = Members.Count(m => m.IsOwner && !m.IsPending);
                if (owners <= 1 && Members.Count(m => !m.IsPending) > 1)
                {
                    await page.DisplayAlertAsync(
                        "Покинуть проект",
                        "Передайте права владельца другому участнику, затем вы сможете выйти.",
                        "OK");
                    return;
                }

                if (owners <= 1 && Members.Count(m => !m.IsPending) <= 1)
                {
                    var del = await page.DisplayAlertAsync(
                        "Единственный участник",
                        "Вы владелец и единственный участник. Удалить проект вместо выхода?",
                        "Удалить", "Отмена");
                    if (del)
                        await Delete();
                    return;
                }
            }

            var ok = await page.DisplayAlertAsync("Покинуть проект", "Вы уверены?", "Да", "Нет");
            if (!ok)
                return;

            await _projectMemberRepository.RemoveMemberAsync(self);
            if (Shell.Current is not null)
                await Shell.Current.GoToAsync("..");
            await AppShell.DisplayToastAsync("Вы вышли из проекта");
        }
    }
}
