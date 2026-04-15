namespace TaskManagementPr.Utilities
{
    public static class TaskPointsSplit
    {
        /// <summary>
        /// Равное распределение баллов по списку исполнителей (остаток от деления — по одному баллу первым в списке).
        /// </summary>
        public static IReadOnlyDictionary<string, int> SharesForAssignees(
            IReadOnlyList<string> assigneeEmails,
            int totalPoints,
            StringComparer comparer)
        {
            var list = assigneeEmails
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => e.Trim().ToLowerInvariant())
                .Distinct(comparer)
                .ToList();

            if (list.Count == 0)
                return new Dictionary<string, int>(comparer);

            var n = list.Count;
            var floor = totalPoints / n;
            var remainder = totalPoints % n;
            var dict = new Dictionary<string, int>(comparer);
            for (var i = 0; i < n; i++)
                dict[list[i]] = floor + (i < remainder ? 1 : 0);

            return dict;
        }

        public static int ShareForEmail(IReadOnlyList<string> assigneeEmails, int totalPoints, string email)
        {
            var dict = SharesForAssignees(assigneeEmails, totalPoints, StringComparer.OrdinalIgnoreCase);
            var key = email.Trim().ToLowerInvariant();
            return dict.TryGetValue(key, out var v) ? v : 0;
        }
    }
}
