using Supabase;

namespace TaskManagementPr.Services
{
    public class AuthService : IAuthService
    {
        private const string CurrentUserEmailPreferenceKey = "current_user_email";
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

        private static string? StoredUserEmail
        {
            get
            {
                var stored = Preferences.Default.Get(CurrentUserEmailPreferenceKey, string.Empty);
                return string.IsNullOrWhiteSpace(stored) ? null : stored;
            }
        }

        public bool IsAuthenticated => _client.Auth.CurrentSession != null ||
            !string.IsNullOrWhiteSpace(StoredUserEmail);

        public string? CurrentUserEmail =>
            _client.Auth.CurrentUser?.Email ??
            StoredUserEmail;

        public async Task<string?> GetEmailAsync()
        {
            await EnsureInitializedAsync();
            var email = _client.Auth.CurrentUser?.Email;
            if (email != null && string.IsNullOrWhiteSpace(StoredUserEmail))
            {
                Preferences.Default.Set(CurrentUserEmailPreferenceKey, email.Trim().ToLowerInvariant());
            }
            return email ?? StoredUserEmail;
        }

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
            if (session?.User?.Email is { } signedInEmail)
                Preferences.Default.Set(CurrentUserEmailPreferenceKey, signedInEmail.Trim().ToLowerInvariant());
            return session != null;
        }

        public async Task<bool> SignUpAsync(string email, string password)
        {
            await EnsureInitializedAsync();
            var session = await _client.Auth.SignUp(email, password);
            if (session?.User?.Email is { } signedUpEmail)
                Preferences.Default.Set(CurrentUserEmailPreferenceKey, signedUpEmail.Trim().ToLowerInvariant());
            return session != null;
        }

        public async Task SignOutAsync()
        {
            await EnsureInitializedAsync();
            await _client.Auth.SignOut();
            Preferences.Default.Remove(CurrentUserEmailPreferenceKey);
        }
    }
}
