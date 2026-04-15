namespace TaskManagementPr.Models
{
    public class ProjectMember
    {
        public int ID { get; set; }
        public int ProjectID { get; set; }
        public string UserEmail { get; set; } = string.Empty;
        public bool IsOwner { get; set; }
        public bool IsPending { get; set; }

        public string DisplayLabel
        {
            get
            {
                var parts = new List<string> { UserEmail };
                if (IsOwner)
                    parts.Add("владелец");
                if (IsPending)
                    parts.Add("приглашён");
                return string.Join(" · ", parts);
            }
        }
    }
}
