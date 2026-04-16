using Microsoft.Extensions.DependencyInjection;

namespace TaskManagementPr
{
    public partial class App : Application
    {
        public static IServiceProvider Services { get; private set; } = default!;

        private readonly IAuthService _authService;

        public App(IAuthService authService, IServiceProvider services)
        {
            InitializeComponent();
            _authService = authService;
            Services = services;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            if (_authService.IsAuthenticated)
                return new Window(new AppShell());

            return new Window(new AuthShell());
        }
    }
}