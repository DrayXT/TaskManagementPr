using TaskManagementPr.Models;

namespace TaskManagementPr.Utilities
{
    public static class TaskStatisticsPoints
    {
        public static string? GetFallbackRecipientEmail(IReadOnlyList<ProjectMember> activeMembers)
        {
            var owner = activeMembers.FirstOrDefault(m => m.IsOwner && !m.IsPending)?.UserEmail;
            if (!string.IsNullOrWhiteSpace(owner))
                return owner.Trim().ToLowerInvariant();

            if (activeMembers.Count == 1)
                return activeMembers[0].UserEmail.Trim().ToLowerInvariant();

            return null;
        }

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

        public static Dictionary<string, int> GetAwardedPoints(ProjectTask task, IReadOnlyList<ProjectMember> activeMembers)
        {
            var awarded = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (task.AssigneeEmails.Count > 0)
            {
                if (HasExplicitShares(task))
                {
                    foreach (var email in task.AssigneeEmails.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        var key = email.Trim().ToLowerInvariant();
                        var points = PointsForAssignee(task, key);
                        if (points <= 0)
                            continue;

                        awarded[key] = points;
                    }

                    return awarded;
                }

                var shares = TaskPointsSplit.SharesForAssignees(
                    task.AssigneeEmails.ToList(),
                    task.RewardPoints,
                    StringComparer.OrdinalIgnoreCase);
                foreach (var kv in shares)
                {
                    if (kv.Value <= 0)
                        continue;

                    awarded[kv.Key] = kv.Value;
                }

                return awarded;
            }

            var fallback = GetFallbackRecipientEmail(activeMembers);
            if (string.IsNullOrWhiteSpace(fallback) || task.RewardPoints <= 0)
                return awarded;

            awarded[fallback] = task.RewardPoints;
            return awarded;
        }

        public static void AddTeamBoardEntries(
            ProjectTask task,
            Dictionary<string, int> board,
            IReadOnlyList<ProjectMember> activeMembers)
        {
            foreach (var kv in GetAwardedPoints(task, activeMembers))
            {
                board.TryGetValue(kv.Key, out var v);
                board[kv.Key] = v + kv.Value;
            }
        }
    }
}
