using TaskManagementPr.Models;

namespace TaskManagementPr.Utilities
{
    public static class TaskStatisticsPoints
    {
        /// <summary>Есть ли явное (ненулевое) распределение по исполнителям.</summary>
        public static bool HasExplicitShares(ProjectTask task)
        {
            if (task.AssigneeEmails.Count == 0)
                return false;

            foreach (var email in task.AssigneeEmails)
            {
                if (task.AssigneePointShares.TryGetValue(email, out var v) && v > 0)
                    return true;
            }

            return false;
        }

        public static int PointsForAssignee(ProjectTask task, string normalizedEmail)
        {
            if (task.AssigneeEmails.Count == 0)
                return 0;

            if (HasExplicitShares(task))
            {
                if (task.AssigneePointShares.TryGetValue(normalizedEmail, out var v))
                    return v;

                foreach (var kv in task.AssigneePointShares)
                {
                    if (kv.Key.Equals(normalizedEmail, StringComparison.OrdinalIgnoreCase))
                        return kv.Value;
                }

                return 0;
            }

            return TaskPointsSplit.ShareForEmail(task.AssigneeEmails.ToList(), task.RewardPoints, normalizedEmail);
        }

        public static void AddTeamBoardEntries(
            ProjectTask task,
            Dictionary<string, int> board,
            IReadOnlyList<ProjectMember> activeMembers)
        {
            if (task.AssigneeEmails.Count == 0)
            {
                foreach (var m in activeMembers)
                {
                    board.TryGetValue(m.UserEmail, out var v);
                    board[m.UserEmail] = v + task.RewardPoints;
                }

                return;
            }

            if (HasExplicitShares(task))
            {
                foreach (var email in task.AssigneeEmails)
                {
                    var pts = PointsForAssignee(task, email.Trim().ToLowerInvariant());
                    var key = email.Trim().ToLowerInvariant();
                    board.TryGetValue(key, out var cur);
                    board[key] = cur + pts;
                }

                return;
            }

            var shares = TaskPointsSplit.SharesForAssignees(
                task.AssigneeEmails.ToList(),
                task.RewardPoints,
                StringComparer.OrdinalIgnoreCase);
            foreach (var kv in shares)
            {
                board.TryGetValue(kv.Key, out var v);
                board[kv.Key] = v + kv.Value;
            }
        }
    }
}
