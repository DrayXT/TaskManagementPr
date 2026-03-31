using TaskManagementPr.Models;
using TaskManagementPr.PageModels;

namespace TaskManagementPr.Pages
{
    public partial class MainPage : ContentPage
    {
        public MainPage(MainPageModel model)
        {
            InitializeComponent();
            BindingContext = model;
        }
    }
}