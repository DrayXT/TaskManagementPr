using Microsoft.Extensions.DependencyInjection;

namespace TaskManagementPr
{
    public partial class App : Application
    {
        private readonly IAuthService _authService;

        public App(IAuthService authService)
        {
            InitializeComponent();
            _authService = authService;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            if (_authService.IsAuthenticated)
                return new Window(new AppShell());

            return new Window(new AuthShell());
        }
    }
}