using System.Text.Json.Serialization;

namespace TaskManagementPr.Models
{
    public class ProjectTask
    {
        public int ID { get; set; }
        public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }

        [JsonIgnore]
        public int ProjectID { get; set; }

        public TaskPriority Priority { get; set; } = TaskPriority.Normal;

        public int RewardPoints { get; set; }

        public DateTime? DueDate { get; set; }

        [JsonIgnore]
        public List<string> AssigneeEmails { get; set; } = [];

        /// <summary>Баллы на конкретного исполнителя (email в нижнем регистре). Сумма не должна превышать <see cref="RewardPoints"/> (максимум бюджета).</summary>
        [JsonIgnore]
        public Dictionary<string, int> AssigneePointShares { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonIgnore]
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    [JsonIgnore]
        public string PriorityLabel => Priority switch
        {
            TaskPriority.Low => "Низкий",
            TaskPriority.Normal => "Обычный",
            TaskPriority.High => "Высокий",
            TaskPriority.Urgent => "Срочный",
            _ => "Обычный"
        };

        [JsonIgnore]
        public string? DueDateShort =>
            DueDate is { } d ? d.ToString("dd.MM.yyyy") : null;

        [JsonIgnore]
        public int AllocatedPointsSum =>
            AssigneePointShares.Values.Sum();

        [JsonIgnore]
        public string Subtitle
        {
            get
            {
                var parts = new List<string>();
                if (RewardPoints > 0)
                {
                    var sum = AllocatedPointsSum;
                    if (sum > 0 && AssigneeEmails.Count > 0)
                        parts.Add($"{sum}/{RewardPoints} б.");
                    else
                        parts.Add($"макс {RewardPoints} б.");
                }

                parts.Add(PriorityLabel);
                if (!string.IsNullOrEmpty(DueDateShort))
                    parts.Add($"до {DueDateShort}");
                return string.Join(" · ", parts);
            }
        }
    }
}