using Supabase;

namespace TaskManagementPr.Services
{
    public class AuthService : IAuthService
    {
        private readonly Supabase.Client _client;
        private bool _initialized;

        public AuthService()
        {
            var options = new SupabaseOptions
            {
                AutoRefreshToken = true
            };
            _client = new Supabase.Client(Constants.SupabaseUrl, Constants.SupabaseAnonKey, options);
        }

        public bool IsAuthenticated => _client.Auth.CurrentSession != null;

        public string? CurrentUserEmail => _client.Auth.CurrentUser?.Email;

        private async Task EnsureInitializedAsync()
        {
            if (_initialized) return;
            await _client.InitializeAsync();
            _initialized = true;
        }

        public async Task<bool> SignInAsync(string email, string password)
        {
            await EnsureInitializedAsync();
            var session = await _client.Auth.SignIn(email, password);
            return session != null;
        }

        public async Task<bool> SignUpAsync(string email, string password)
        {
            await EnsureInitializedAsync();
            var session = await _client.Auth.SignUp(email, password);
            return session != null;
        }

        public async Task SignOutAsync()
        {
            await EnsureInitializedAsync();
            await _client.Auth.SignOut();
        }
    }
}
