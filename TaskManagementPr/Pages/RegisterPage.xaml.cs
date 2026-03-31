namespace TaskManagementPr.Pages
{
    public partial class RegisterPage : ContentPage
    {
        public RegisterPage(RegisterPageModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
        }
    }
}
