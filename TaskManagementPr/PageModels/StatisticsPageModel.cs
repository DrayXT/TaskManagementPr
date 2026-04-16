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
    public class SharedProjectRow
    {
        public string ProjectName { get; set; } = string.Empty;
        public string MembersSummary { get; set; } = string.Empty;
    }

    public partial class StatisticsPageModel : ObservableObject
    {
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

        private static Dictionary<string, int> GetAwardedPointsForPersonalContext(
            string me,
            ProjectTask task,
            IReadOnlyList<ProjectMember> activeMembers)
        {
            var awardedPoints = TaskStatisticsPoints.GetAwardedPoints(task, activeMembers);
            if (awardedPoints.Count > 0)
                return awardedPoints;

            if (task.AssigneeEmails.Count == 0)
            {
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    [me] = TaskStatisticsPoints.GetEffectiveRewardPoints(task)
                };
            }

            return awardedPoints;
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
                awardedPoints.TryGetValue(ProjectVisibilityRules.LocalOwnerPlaceholderEmail, out var legacyPoints))
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
                var me = ProjectVisibilityRules.Normalize(realEmail) ?? ProjectVisibilityRules.LocalOwnerPlaceholderEmail;
                CurrentUserDisplay = realEmail ?? $"не авторизован ({ProjectVisibilityRules.LocalOwnerPlaceholderEmail})";

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
                    if (!ProjectVisibilityRules.ShouldIncludeProject(me, activeMembers, project.Tasks))
                        continue;

                    var legacyOwnerAlias = ProjectVisibilityRules.IsLegacyLocalOnlyPlaceholder(activeMembers) &&
                        !me.Equals(ProjectVisibilityRules.LocalOwnerPlaceholderEmail, StringComparison.OrdinalIgnoreCase);

                    var isMySharedProject = activeMembers.Count >= 2 &&
                        activeMembers.Any(m => m.UserEmail.Equals(me, StringComparison.OrdinalIgnoreCase));

                    if (isMySharedProject)
                    {
                        var visibleMembers = activeMembers
                            .Where(m => !m.UserEmail.Equals(ProjectVisibilityRules.LocalOwnerPlaceholderEmail, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        sharedRows.Add(new SharedProjectRow
                        {
                            ProjectName = project.Name,
                            MembersSummary = visibleMembers.Count > 0
                                ? string.Join(", ", visibleMembers.Select(m => m.DisplayLabel))
                                : "нет активных участников"
                        });
                    }

                    foreach (var task in project.Tasks.Where(t => t.IsCompleted))
                    {
                        var personalAwardedPoints = GetAwardedPointsForPersonalContext(me, task, activeMembers);
                        if (IsPersonalCredit(task, me, personalAwardedPoints, legacyOwnerAlias))
                        {
                            personalTasks++;
                            personalPoints += GetPersonalPoints(me, task, personalAwardedPoints, legacyOwnerAlias);
                        }

                        if (!isMySharedProject)
                            continue;

                        var teamAwardedPoints = TaskStatisticsPoints.GetAwardedPoints(task, activeMembers);
                        teamTasks++;
                        teamPoints += teamAwardedPoints.Values.Sum();
                    }
                }

                foreach (var task in (await _taskRepository.ListAsync()).Where(t => t.IsCompleted && t.ProjectID == 0))
                {
                    var awardedPoints = GetAwardedPointsForPersonalContext(me, task, []);
                    if (awardedPoints.TryGetValue(me, out var personalAward) && personalAward > 0)
                    {
                        personalTasks++;
                        personalPoints += personalAward;
                    }
                }

                PersonalTasksCompleted = personalTasks;
                PersonalPoints = personalPoints;
                TeamCompletedTasksInShared = teamTasks;
                TeamPointsInShared = teamPoints;
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
            IReadOnlyDictionary<string, int> awardedPoints,
            bool treatLegacyOwnerAsCurrentUser)
        {
            if (task.AssigneeEmails.Count > 0)
                return awardedPoints.ContainsKey(me);

            if (awardedPoints.ContainsKey(me))
                return true;

            return treatLegacyOwnerAsCurrentUser &&
                awardedPoints.ContainsKey(ProjectVisibilityRules.LocalOwnerPlaceholderEmail);
        }
    }
}
