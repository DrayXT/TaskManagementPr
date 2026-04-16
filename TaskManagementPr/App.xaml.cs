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
                return new Window(CreateAppShell());

            return new Window(CreateAuthShell());
        }

        public static AppShell CreateAppShell() =>
            Services.GetRequiredService<AppShell>();

        public static AuthShell CreateAuthShell() =>
            Services.GetRequiredService<AuthShell>();

        public static void SwitchRootPage(Page page)
        {
            if (Current?.Windows.Count > 0)
                Current.Windows[0].Page = page;
        }
    }
}