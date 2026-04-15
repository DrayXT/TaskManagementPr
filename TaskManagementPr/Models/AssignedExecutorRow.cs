using CommunityToolkit.Mvvm.ComponentModel;

namespace TaskManagementPr.Models
{
    /// <summary>Исполнитель задачи с введённым вручную числом баллов.</summary>
    public partial class AssignedExecutorRow : ObservableObject
    {
        public string Email { get; }

        [ObservableProperty]
        private string _pointsText = "0";

        public AssignedExecutorRow(string email, int initialPoints = 0)
        {
            Email = email;
            PointsText = initialPoints.ToString();
        }

        public int GetParsedPoints() =>
            int.TryParse(PointsText, out var v) ? Math.Max(0, v) : 0;
    }
}
