namespace TaskManagementPr.Pages
{
    public partial class LoginPage : ContentPage
    {
        public LoginPage(LoginPageModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
        }
    }
}
