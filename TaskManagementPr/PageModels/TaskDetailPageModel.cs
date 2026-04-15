using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using TaskManagementPr.Data;
using TaskManagementPr.Messaging;
using TaskManagementPr.Models;

namespace TaskManagementPr.PageModels
{
    public partial class TaskDetailPageModel : ObservableObject, IQueryAttributable
    {
        public const string ProjectQueryKey = "project";
        private ProjectTask? _task;
        private bool _canDelete;
        private readonly ProjectRepository _projectRepository;
        private readonly TaskRepository _taskRepository;
        private readonly ModalErrorHandler _errorHandler;

        [ObservableProperty]
        private string _title = string.Empty;

        [ObservableProperty]
        private bool _isCompleted;

        /// <summary>Максимум баллов (бюджет), по умолчанию 100 — вводится в поле.</summary>
        [ObservableProperty]
        private string _rewardBudgetMaxText = "100";

        [ObservableProperty]
        private bool _hasDueDate;

        [ObservableProperty]
        private DateTime _dueDatePicker = DateTime.Today;

        [ObservableProperty]
        private int _priorityIndex = 1;

        [ObservableProperty]
        private ObservableCollection<string> _priorityOptions = [];

        [ObservableProperty]
        private List<Project> _projects = [];

        [ObservableProperty]
        private Project? _project;

        [ObservableProperty]
        private int _selectedProjectIndex = -1;

        [ObservableProperty]
        private bool _isExistingProject;

        [ObservableProperty]
        private string _newAssigneeEmail = string.Empty;

        [ObservableProperty]
        private ObservableCollection<AssignedExecutorRow> _assignedExecutors = [];

        [ObservableProperty]
        private string _pointsHint = string.Empty;

        public TaskDetailPageModel(
            ProjectRepository projectRepository,
            TaskRepository taskRepository,
            ModalErrorHandler errorHandler)
        {
            _projectRepository = projectRepository;
            _taskRepository = taskRepository;
            _errorHandler = errorHandler;

            PriorityOptions =
            [
                "Низкий — можно отложить",
                "Обычный — в работе",
                "Высокий — важно",
                "Срочно — сделать в первую очередь"
            ];

            AssignedExecutors.CollectionChanged += (_, _) => RefreshPointsHint();
        }

        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            LoadTaskAsync(query).FireAndForgetSafeAsync(_errorHandler);
        }

        private void RegisterRow(AssignedExecutorRow row)
        {
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AssignedExecutorRow.PointsText))
                    RefreshPointsHint();
            };
        }

        private async Task LoadTaskAsync(IDictionary<string, object> query)
        {
            if (query.TryGetValue(ProjectQueryKey, out var project))
                Project = (Project)project;

            int taskId = 0;

            if (query.ContainsKey("id"))
            {
                taskId = Convert.ToInt32(query["id"]);
                _task = await _taskRepository.GetAsync(taskId);

                if (_task is null)
                {
                    _errorHandler.HandleError(new Exception($"Task Id {taskId} isn't valid."));
                    return;
                }

                Project = _task.ProjectID > 0
                    ? await _projectRepository.GetAsync(_task.ProjectID)
                    : null;
            }
            else
            {
                _task = new ProjectTask();
            }

            if (Project?.ID == 0)
            {
                IsExistingProject = false;
            }
            else
            {
                var dbProjects = await _projectRepository.ListAsync();
                var unassigned = new Project { ID = 0, Name = "Без проекта" };
                Projects = [unassigned, ..dbProjects];
                IsExistingProject = true;
            }

            if (Projects.Count > 0)
            {
                if (Project is not null)
                    SelectedProjectIndex = Projects.FindIndex(p => p.ID == Project.ID);
                else if (_task is not null)
                    SelectedProjectIndex = Projects.FindIndex(p => p.ID == _task.ProjectID);
            }

            AssignedExecutors.Clear();
            if (taskId > 0)
            {
                if (_task is null)
                {
                    _errorHandler.HandleError(new Exception($"Task with id {taskId} could not be found."));
                    return;
                }

                Title = _task.Title;
                IsCompleted = _task.IsCompleted;
                RewardBudgetMaxText = (_task.RewardPoints > 0 ? _task.RewardPoints : 100).ToString();
                HasDueDate = _task.DueDate.HasValue;
                DueDatePicker = _task.DueDate?.Date ?? DateTime.Today;
                PriorityIndex = (int)_task.Priority;
                if (PriorityIndex < 0 || PriorityIndex > 3)
                    PriorityIndex = 1;

                foreach (var email in _task.AssigneeEmails.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var pts = _task.AssigneePointShares.TryGetValue(email, out var p)
                        ? p
                        : _task.AssigneePointShares
                            .FirstOrDefault(kv => kv.Key.Equals(email, StringComparison.OrdinalIgnoreCase))
                            .Value;
                    var row = new AssignedExecutorRow(email, pts);
                    RegisterRow(row);
                    AssignedExecutors.Add(row);
                }

                CanDelete = true;
            }
            else
            {
                _task = new ProjectTask { ProjectID = Project?.ID ?? 0, RewardPoints = 100 };
                RewardBudgetMaxText = "100";
                HasDueDate = false;
                DueDatePicker = DateTime.Today;
            }

            RefreshPointsHint();
        }

        partial void OnRewardBudgetMaxTextChanged(string value) =>
            RefreshPointsHint();

        partial void OnHasDueDateChanged(bool value) =>
            OnPropertyChanged(nameof(DueDateSectionVisible));

        public bool DueDateSectionVisible => HasDueDate;

        private int ParseBudgetMax()
        {
            if (!int.TryParse(RewardBudgetMaxText.Trim(), out var v) || v < 1)
                return 100;
            return Math.Clamp(v, 1, 10_000);
        }

        private void RefreshPointsHint()
        {
            var max = ParseBudgetMax();
            var sum = AssignedExecutors.Sum(r => r.GetParsedPoints());
            var n = AssignedExecutors.Count;

            if (n <= 0)
            {
                PointsHint =
                    $"Максимум баллов: {max}. Исполнителей нет — при завершении учёт баллов по правилам проекта.";
                return;
            }

            if (sum > max)
            {
                PointsHint = $"Внимание: сейчас распределено {sum} балл(ов), а максимум {max}. Уменьшите значения или увеличьте максимум.";
                return;
            }

            if (sum == max)
            {
                PointsHint = $"Максимум {max} балл(ов) — распределено полностью ({n} исполн.).";
                return;
            }

            PointsHint =
                $"Максимум {max} балл(ов). Распределено: {sum}. Осталось свободно: {max - sum}. (Если оставить нули у всех при нескольких людях, при подсчёте поделится поровну.)";
        }

        partial void OnProjectChanged(Project? value)
        {
            if (_task is not null && value is not null)
                _task.ProjectID = value.ID;
            RefreshPointsHint();
        }

        partial void OnSelectedProjectIndexChanged(int value)
        {
            if (Projects.Count > value && value >= 0)
            {
                Project = Projects[value];
                if (_task is not null)
                    _task.ProjectID = Project.ID;
                RefreshPointsHint();
            }
        }

        public bool CanDelete
        {
            get => _canDelete;
            set
            {
                _canDelete = value;
                DeleteCommand.NotifyCanExecuteChanged();
            }
        }

        [RelayCommand]
        private async Task AddAssignee()
        {
            var raw = NewAssigneeEmail.Trim();
            if (string.IsNullOrEmpty(raw))
            {
                await ShowMessageAsync("Пусто", "Введите email.");
                return;
            }

            if (!LooksLikeEmail(raw))
            {
                await ShowMessageAsync("Email", "Введите корректный email (например, name@site.com).");
                return;
            }

            var normalized = raw.ToLowerInvariant();
            if (AssignedExecutors.Any(r => r.Email.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            {
                NewAssigneeEmail = string.Empty;
                await ShowMessageAsync("Уже добавлен", "Этот email уже в списке.");
                return;
            }

            var row = new AssignedExecutorRow(normalized, 0);
            RegisterRow(row);
            AssignedExecutors.Add(row);
            NewAssigneeEmail = string.Empty;
            RefreshPointsHint();
        }

        private static bool LooksLikeEmail(string s)
        {
            var at = s.IndexOf('@');
            if (at <= 0 || at >= s.Length - 1)
                return false;
            return s.AsSpan(at).Contains('.');
        }

        [RelayCommand]
        private void RemoveAssignee(AssignedExecutorRow? row)
        {
            if (row is null)
                return;

            var found = AssignedExecutors.FirstOrDefault(r =>
                r.Email.Equals(row.Email, StringComparison.OrdinalIgnoreCase));
            if (found is not null)
                AssignedExecutors.Remove(found);

            RefreshPointsHint();
        }

        private static async Task ShowMessageAsync(string title, string message)
        {
            var page = Shell.Current?.CurrentPage;
            if (page is not null)
                await page.DisplayAlertAsync(title, message, "OK");
        }

        [RelayCommand]
        private async Task Save()
        {
            if (_task is null)
            {
                _errorHandler.HandleError(
                    new Exception("Task or project is null. The task could not be saved."));

                return;
            }

            var max = ParseBudgetMax();
            var sum = AssignedExecutors.Sum(r => r.GetParsedPoints());
            if (sum > max)
            {
                await ShowMessageAsync(
                    "Слишком много баллов",
                    $"Сумма по людям ({sum}) больше максимума ({max}). Исправьте распределение или увеличьте максимум.");
                return;
            }

            if (AssignedExecutors.Count == 1 && sum == 0)
                AssignedExecutors[0].PointsText = max.ToString();

            sum = AssignedExecutors.Sum(r => r.GetParsedPoints());

            _task.Title = Title;
            _task.IsCompleted = IsCompleted;
            _task.RewardPoints = max;
            _task.DueDate = HasDueDate ? DueDatePicker.Date : null;
            _task.Priority = (TaskPriority)Math.Clamp(PriorityIndex, 0, 3);

            if (Projects.Count > 0 && SelectedProjectIndex >= 0 && SelectedProjectIndex < Projects.Count)
            {
                var p = Projects[SelectedProjectIndex];
                _task.ProjectID = p.ID;
                Project = p;
            }

            _task.AssigneePointShares.Clear();
            _task.AssigneeEmails.Clear();
            foreach (var row in AssignedExecutors)
            {
                var email = row.Email.Trim().ToLowerInvariant();
                var pts = row.GetParsedPoints();
                if (string.IsNullOrEmpty(email))
                    continue;
                _task.AssigneeEmails.Add(email);
                _task.AssigneePointShares[email] = pts;
            }

            if (Project?.ID == _task.ProjectID && Project.Tasks is not null && !Project.Tasks.Contains(_task))
                Project.Tasks.Add(_task);

            await _taskRepository.SaveItemAsync(_task);
            WeakReferenceMessenger.Default.Send(new TaskDataChangedMessage());

            if (Shell.Current is not null)
                await Shell.Current.GoToAsync("..?refresh=true");

            if (_task.ID > 0)
                await AppShell.DisplayToastAsync("Задача сохранена");
        }

        [RelayCommand(CanExecute = nameof(CanDelete))]
        private async Task Delete()
        {
            if (_task is null)
            {
                _errorHandler.HandleError(
                    new Exception("Task is null. The task could not be deleted."));

                return;
            }

            if (Project?.Tasks is not null && Project.Tasks.Contains(_task))
                Project.Tasks.Remove(_task);

            if (_task.ID > 0)
                await _taskRepository.DeleteItemAsync(_task);

            WeakReferenceMessenger.Default.Send(new TaskDataChangedMessage());
            if (Shell.Current is not null)
                await Shell.Current.GoToAsync("..?refresh=true");
            await AppShell.DisplayToastAsync("Задача удалена");
        }

        [RelayCommand]
        private async Task RemoveFromProject()
        {
            if (_task is null || _task.ID == 0)
            {
                _errorHandler.HandleError(new Exception("Сначала сохраните задачу, затем можно убрать из проекта."));
                return;
            }

            _task.ProjectID = 0;
            Project = Projects.FirstOrDefault(p => p.ID == 0);
            SelectedProjectIndex = Projects.FindIndex(p => p.ID == 0);
            AssignedExecutors.Clear();
            _task.AssigneeEmails.Clear();
            _task.AssigneePointShares.Clear();
            RefreshPointsHint();
            await _taskRepository.SaveItemAsync(_task);
            WeakReferenceMessenger.Default.Send(new TaskDataChangedMessage());
            await AppShell.DisplayToastAsync("Задача убрана из проекта");
        }
    }
}
