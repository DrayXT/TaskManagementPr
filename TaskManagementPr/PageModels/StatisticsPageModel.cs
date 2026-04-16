using System.Collections.ObjectModel;
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
    public class MemberStatRow
    {
        public string Email { get; set; } = string.Empty;
        public int Points { get; set; }
    }

    public class SharedProjectRow
    {
        public string ProjectName { get; set; } = string.Empty;
        public string MembersSummary { get; set; } = string.Empty;
    }

    public partial class StatisticsPageModel : ObservableObject
    {
        /// <summary>Совпадает с запасным владельцем в <see cref="ProjectMemberRepository"/> при отсутствии email.</summary>
        private const string LocalOwnerPlaceholderEmail = "local@owner.app";

        private readonly ProjectRepository _projectRepository;
        private readonly TaskRepository _taskRepository;
        private readonly ProjectMemberRepository _projectMemberRepository;
        private readonly IAuthService _authService;
        private readonly ModalErrorHandler _errorHandler;

        [ObservableProperty]
        private string _currentUserDisplay = string.Empty;

        [ObservableProperty]
        private int _personalTasksCompleted;

        [ObservableProperty]
        private int _personalPoints;

        [ObservableProperty]
        private int _teamCompletedTasksInShared;

        [ObservableProperty]
        private int _teamPointsInShared;

        [ObservableProperty]
        private ObservableCollection<MemberStatRow> _teamLeaderboard = [];

        [ObservableProperty]
        private ObservableCollection<SharedProjectRow> _sharedProjects = [];

        [ObservableProperty]
        private bool _isBusy;

        public StatisticsPageModel(
            ProjectRepository projectRepository,
            TaskRepository taskRepository,
            ProjectMemberRepository projectMemberRepository,
            IAuthService authService,
            ModalErrorHandler errorHandler)
        {
            _projectRepository = projectRepository;
            _taskRepository = taskRepository;
            _projectMemberRepository = projectMemberRepository;
            _authService = authService;
            _errorHandler = errorHandler;

            WeakReferenceMessenger.Default.Register<StatisticsPageModel, TaskDataChangedMessage>(
                this,
                (r, _msg) =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        _ = r.LoadAsync();
                    });
                });
        }

        private static string? Normalize(string? email) =>
            string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

        /// <summary>Один активный участник — запасной локальный владелец (проект создан до входа в аккаунт).</summary>
        private static bool IsLegacyLocalOnlyPlaceholder(IReadOnlyList<ProjectMember> activeMembers) =>
            activeMembers.Count == 1 &&
            activeMembers[0].UserEmail.Equals(LocalOwnerPlaceholderEmail, StringComparison.OrdinalIgnoreCase);

        /// <summary>Показывать проект в личной статистике: вы в участниках, вы назначены на задачу или это локальный проект владельца до входа.</summary>
        private bool ShouldIncludeProject(string me, IReadOnlyList<ProjectMember> activeMembers, IReadOnlyList<ProjectTask> tasks)
        {
            if (activeMembers.Count == 0)
                return true;

            if (activeMembers.Any(m => m.UserEmail.Equals(me, StringComparison.OrdinalIgnoreCase)))
                return true;

            if (tasks.Any(t => t.AssigneeEmails.Any(e => e.Equals(me, StringComparison.OrdinalIgnoreCase))))
                return true;

            if (me.Equals(LocalOwnerPlaceholderEmail, StringComparison.OrdinalIgnoreCase))
                return false;

            return IsLegacyLocalOnlyPlaceholder(activeMembers);
        }

        private static string NormalizeForBoard(string email) =>
            email.Trim().ToLowerInvariant();

        private static string NormalizeBoardKey(
            string email,
            string me,
            bool treatLegacyOwnerAsCurrentUser)
        {
            if (treatLegacyOwnerAsCurrentUser &&
                email.Equals(LocalOwnerPlaceholderEmail, StringComparison.OrdinalIgnoreCase))
            {
                return me;
            }

            return NormalizeForBoard(email);
        }

        private static int GetPersonalPoints(
            string me,
            ProjectTask task,
            IReadOnlyDictionary<string, int> awardedPoints,
            bool treatLegacyOwnerAsCurrentUser)
        {
            if (awardedPoints.TryGetValue(me, out var points))
                return points;

            if (treatLegacyOwnerAsCurrentUser &&
                task.AssigneeEmails.Count == 0 &&
                awardedPoints.TryGetValue(LocalOwnerPlaceholderEmail, out var legacyPoints))
            {
                return legacyPoints;
            }

            return 0;
        }

        [RelayCommand]
        private async Task Appearing()
        {
            await LoadAsync();
        }

        /// <summary>Переключение вкладки Shell не всегда вызывает Appearing — NavigatedTo надёжнее.</summary>
        [RelayCommand]
        private async Task NavigatedTo()
        {
            await LoadAsync();
        }

        private async Task LoadAsync()
        {
            try
            {
                IsBusy = true;
                
                var realEmail = await _authService.GetEmailAsync();
                var me = Normalize(realEmail) ?? "local@owner.app";
                CurrentUserDisplay = realEmail ?? "не авторизован (local@owner.app)";

                await _projectMemberRepository.ActivatePendingForEmailAsync(realEmail);

                var projects = await _projectRepository.ListAsync();

                var personalTasks = 0;
                var personalPoints = 0;
                var teamTasks = 0;
                var teamPoints = 0;
                var sharedRows = new List<SharedProjectRow>();

                foreach (var project in projects)
                {
                    var activeMembers = project.Members.Where(m => !m.IsPending).ToList();
                    if (!ShouldIncludeProject(me, activeMembers, project.Tasks))
                        continue;

                    var legacyOwnerAlias = IsLegacyLocalOnlyPlaceholder(activeMembers) &&
                        !me.Equals(LocalOwnerPlaceholderEmail, StringComparison.OrdinalIgnoreCase);
                    var namedUserInMembers = activeMembers.Any(m =>
                        m.UserEmail.Equals(me, StringComparison.OrdinalIgnoreCase));
                    var noAssigneeTaskCountsForPersonal = activeMembers.Count == 0 || namedUserInMembers ||
                        legacyOwnerAlias;

                    var isShared = true; // Теперь показываем все проекты
                    if (isShared)
                    {
                        var visibleMembers = activeMembers
                            .Where(m => !m.UserEmail.Equals(LocalOwnerPlaceholderEmail, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        var names = string.Join(", ", visibleMembers.Select(m =>
                            m.IsPending ? $"{m.UserEmail} (ожидает)" : m.UserEmail));
                        if (string.IsNullOrWhiteSpace(names))
                            names = "нет активных участников";
                        sharedRows.Add(new SharedProjectRow
                        {
                            ProjectName = project.Name,
                            MembersSummary = names
                        });
                    }

                    foreach (var task in project.Tasks.Where(t => t.IsCompleted))
                    {
                        TaskStatisticsPoints.ApplyCompletionDefaults(task);
                        var awardedPoints = TaskStatisticsPoints.GetAwardedPoints(task, activeMembers);
                        if (awardedPoints.Count == 0 &&
                            task.AssigneeEmails.Count == 0 &&
                            noAssigneeTaskCountsForPersonal)
                        {
                            awardedPoints = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                            {
                                [me] = TaskStatisticsPoints.GetEffectiveRewardPoints(task)
                            };
                        }

                        if (IsPersonalCredit(task, me, noAssigneeTaskCountsForPersonal, awardedPoints, legacyOwnerAlias))
                        {
                            personalTasks++;
                            personalPoints += GetPersonalPoints(me, task, awardedPoints, legacyOwnerAlias);
                        }

                        if (!isShared)
                            continue;

                        teamTasks++;
                        teamPoints += awardedPoints.Values.Sum();
                    }
                }

                // Задачи без проекта (ProjectID == 0) не попадают в project.Tasks и раньше терялись в статистике.
                var allTasks = await _taskRepository.ListAsync();
                foreach (var task in allTasks.Where(t => t.IsCompleted && t.ProjectID == 0))
                {
                    TaskStatisticsPoints.ApplyCompletionDefaults(task);
                    var awardedPoints = TaskStatisticsPoints.GetAwardedPoints(task, []);
                    if (awardedPoints.TryGetValue(me, out var personalAward))
                    {
                        personalTasks++;
                        personalPoints += personalAward;
                    }

                    teamTasks++;
                    teamPoints += awardedPoints.Values.Sum();
                }

                PersonalTasksCompleted = personalTasks;
                PersonalPoints = personalPoints;
                TeamCompletedTasksInShared = teamTasks;
                TeamPointsInShared = teamPoints;
                TeamLeaderboard = [];

                SharedProjects = new ObservableCollection<SharedProjectRow>(sharedRows);
            }
            catch (Exception e)
            {
                _errorHandler.HandleError(e);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static bool IsPersonalCredit(
            ProjectTask task,
            string me,
            bool noAssigneeTaskCountsForPersonal,
            IReadOnlyDictionary<string, int> awardedPoints,
            bool treatLegacyOwnerAsCurrentUser)
        {
            if (task.AssigneeEmails.Count > 0)
                return awardedPoints.ContainsKey(me);

            if (noAssigneeTaskCountsForPersonal)
                return true;

            return treatLegacyOwnerAsCurrentUser &&
                awardedPoints.ContainsKey(LocalOwnerPlaceholderEmail);
        }
    }
}
