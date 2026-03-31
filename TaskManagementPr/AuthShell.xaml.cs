namespace TaskManagementPr
{
    public partial class AuthShell : Shell
    {
        public AuthShell()
        {
            InitializeComponent();
            Routing.RegisterRoute("register", typeof(Pages.RegisterPage));
        }
    }
}
