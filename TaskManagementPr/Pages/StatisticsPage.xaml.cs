using TaskManagementPr.PageModels;

namespace TaskManagementPr.Pages
{
    public partial class StatisticsPage : ContentPage
    {
        public StatisticsPage(StatisticsPageModel model)
        {
            InitializeComponent();
            BindingContext = model;
        }
    }
}
