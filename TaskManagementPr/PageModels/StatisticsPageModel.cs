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
        private readonly ProjectRepository _projectRepository;
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
            ProjectMemberRepository projectMemberRepository,
            IAuthService authService,
            ModalErrorHandler errorHandler)
        {
            _projectRepository = projectRepository;
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
                var me = Normalize(_authService.CurrentUserEmail) ?? "local@owner.app";
                CurrentUserDisplay = _authService.CurrentUserEmail ?? "не авторизован (local@owner.app)";

                await _projectMemberRepository.ActivatePendingForEmailAsync(_authService.CurrentUserEmail);

                var projects = await _projectRepository.ListAsync();

                var personalTasks = 0;
                var personalPoints = 0;
                var teamTasks = 0;
                var teamPoints = 0;
                var board = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var sharedRows = new List<SharedProjectRow>();

                foreach (var project in projects)
                {
                    var activeMembers = project.Members.Where(m => !m.IsPending).ToList();
                    if (activeMembers.Count > 0 &&
                        !activeMembers.Any(m => m.UserEmail.Equals(me, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var isShared = activeMembers.Count >= 2;
                    if (isShared)
                    {
                        var names = string.Join(", ", activeMembers.Select(m =>
                            m.IsPending ? $"{m.UserEmail} (ожидает)" : m.UserEmail));
                        sharedRows.Add(new SharedProjectRow
                        {
                            ProjectName = project.Name,
                            MembersSummary = names
                        });
                    }

                    foreach (var task in project.Tasks.Where(t => t.IsCompleted))
                    {
                        if (IsPersonalCredit(task, me, activeMembers))
                        {
                            personalTasks++;
                            if (task.AssigneeEmails.Count > 0)
                                personalPoints += TaskStatisticsPoints.PointsForAssignee(task, me);
                            else
                                personalPoints += task.RewardPoints;
                        }

                        if (!isShared)
                            continue;

                        teamTasks++;
                        teamPoints += task.RewardPoints;

                        TaskStatisticsPoints.AddTeamBoardEntries(task, board, activeMembers);
                    }
                }

                PersonalTasksCompleted = personalTasks;
                PersonalPoints = personalPoints;
                TeamCompletedTasksInShared = teamTasks;
                TeamPointsInShared = teamPoints;

                TeamLeaderboard = new ObservableCollection<MemberStatRow>(
                    board.OrderByDescending(kv => kv.Value)
                        .Select(kv => new MemberStatRow { Email = kv.Key, Points = kv.Value }));

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

        private static bool IsPersonalCredit(ProjectTask task, string me, List<ProjectMember> activeMembers)
        {
            if (task.AssigneeEmails.Count > 0)
                return task.AssigneeEmails.Any(e => e.Equals(me, StringComparison.OrdinalIgnoreCase));

            return activeMembers.Count == 0 ||
                   activeMembers.Any(m => m.UserEmail.Equals(me, StringComparison.OrdinalIgnoreCase));
        }
    }
}
