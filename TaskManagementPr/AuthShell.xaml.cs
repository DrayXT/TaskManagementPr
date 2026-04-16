namespace TaskManagementPr
{
    public partial class AuthShell : Shell
    {
        private static bool _routesRegistered;

        public AuthShell()
        {
            InitializeComponent();
            if (_routesRegistered)
                return;

            Routing.RegisterRoute("register", typeof(Pages.RegisterPage));
            _routesRegistered = true;
        }
    }
}
