using TaskManagementPr.Models;

namespace TaskManagementPr.Utilities
{
    public static class TaskStatisticsPoints
    {
        public static int GetEffectiveRewardPoints(ProjectTask task) =>
            task.RewardPoints > 0 ? task.RewardPoints : 100;

        private static Dictionary<string, int> SplitEvenly(IReadOnlyList<string> emails, int totalPoints)
        {
            var awarded = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (emails.Count == 0 || totalPoints <= 0)
                return awarded;

            var normalized = emails
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(e => e.Trim().ToLowerInvariant())
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .ToList();

            if (normalized.Count == 0)
                return awarded;

            var perUser = totalPoints / normalized.Count;
            var remainder = totalPoints % normalized.Count;
            for (var i = 0; i < normalized.Count; i++)
            {
                var bonus = i < remainder ? 1 : 0;
                var value = perUser + bonus;
                if (value > 0)
                    awarded[normalized[i]] = value;
            }

            return awarded;
        }

        public static void ApplyCompletionDefaults(ProjectTask task)
        {
            task.RewardPoints = GetEffectiveRewardPoints(task);

            var normalizedAssignees = task.AssigneeEmails
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            task.AssigneeEmails.Clear();
            foreach (var email in normalizedAssignees)
                task.AssigneeEmails.Add(email);

            if (normalizedAssignees.Count == 0 || HasPositiveShares(task))
                return;

            task.AssigneePointShares.Clear();
            var shares = SplitEvenly(normalizedAssignees, task.RewardPoints);
            foreach (var kv in shares)
                task.AssigneePointShares[kv.Key] = kv.Value;
        }

        public static string? GetFallbackRecipientEmail(IReadOnlyList<ProjectMember> activeMembers)
        {
            var owner = activeMembers.FirstOrDefault(m => m.IsOwner && !m.IsPending)?.UserEmail;
            if (!string.IsNullOrWhiteSpace(owner))
                return owner.Trim().ToLowerInvariant();

            if (activeMembers.Count == 1)
                return activeMembers[0].UserEmail.Trim().ToLowerInvariant();

            return null;
        }

        /// <summary>Есть ли ненулевые баллы у кого-то из назначенных исполнителей.</summary>
        public static bool HasPositiveShares(ProjectTask task)
        {
            if (task.AssigneeEmails.Count == 0)
                return false;

            foreach (var email in task.AssigneeEmails)
            {
                foreach (var kv in task.AssigneePointShares)
                {
                    if (kv.Key.Equals(email, StringComparison.OrdinalIgnoreCase) && kv.Value > 0)
                        return true;
                }
            }

            return false;
        }

        public static int PointsForAssignee(ProjectTask task, string normalizedEmail)
        {
            if (task.AssigneeEmails.Count == 0)
                return 0;

            if (task.AssigneePointShares.TryGetValue(normalizedEmail, out var v))
                return Math.Max(0, v);

            foreach (var kv in task.AssigneePointShares)
            {
                if (kv.Key.Equals(normalizedEmail, StringComparison.OrdinalIgnoreCase))
                    return Math.Max(0, kv.Value);
            }

            return 0;
        }

        public static Dictionary<string, int> GetAwardedPoints(ProjectTask task, IReadOnlyList<ProjectMember> activeMembers)
        {
            var awarded = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (task.AssigneeEmails.Count > 0)
            {
                var emails = task.AssigneeEmails;
                if (!HasPositiveShares(task))
                    return SplitEvenly(emails, GetEffectiveRewardPoints(task));

                foreach (var key in emails
                             .Distinct(StringComparer.OrdinalIgnoreCase)
                             .Select(email => email.Trim().ToLowerInvariant()))
                {
                    var points = PointsForAssignee(task, key);
                    if (points <= 0)
                        continue;

                    awarded[key] = points;
                }

                return awarded;
            }

            var fallback = GetFallbackRecipientEmail(activeMembers);
            var effectiveReward = GetEffectiveRewardPoints(task);
            if (string.IsNullOrWhiteSpace(fallback) || effectiveReward <= 0)
                return awarded;

            awarded[fallback] = effectiveReward;
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
