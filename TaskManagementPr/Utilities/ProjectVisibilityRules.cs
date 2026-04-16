using TaskManagementPr.Models;

namespace TaskManagementPr.Utilities
{
    public static class ProjectVisibilityRules
    {
        public const string LocalOwnerPlaceholderEmail = "local@owner.app";

        public static string? Normalize(string? email) =>
            string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

        public static bool IsLegacyLocalOnlyPlaceholder(IReadOnlyList<ProjectMember> activeMembers) =>
            activeMembers.Count == 1 &&
            activeMembers[0].UserEmail.Equals(LocalOwnerPlaceholderEmail, StringComparison.OrdinalIgnoreCase);

        public static bool ShouldIncludeProject(string me, IReadOnlyList<ProjectMember> activeMembers, IReadOnlyList<ProjectTask> tasks)
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

        public static bool ShouldIncludeTask(string me, ProjectTask task, IReadOnlySet<int> visibleProjectIds)
        {
            if (task.ProjectID > 0)
                return visibleProjectIds.Contains(task.ProjectID);

            if (task.AssigneeEmails.Any(e => e.Equals(me, StringComparison.OrdinalIgnoreCase)))
                return true;

            return task.AssigneeEmails.Count == 0;
        }
    }
}
