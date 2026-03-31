using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TaskManagementPr.PageModels
{
    public partial class LoginPageModel : ObservableObject
    {
        private readonly IAuthService _authService;

        [ObservableProperty]
        private string _email = string.Empty;

        [ObservableProperty]
        private string _password = string.Empty;

        [ObservableProperty]
        private string? _errorMessage;

        [ObservableProperty]
        private bool _isBusy;

        public LoginPageModel(IAuthService authService)
        {
            _authService = authService;
        }

        [RelayCommand]
        private async Task SignIn()
        {
            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "Введите email и пароль";
                return;
            }

            try
            {
                IsBusy = true;
                ErrorMessage = null;

                var success = await _authService.SignInAsync(Email.Trim(), Password);

                if (success)
                {
                    Application.Current!.Windows[0].Page = new AppShell();
                }
                else
                {
                    ErrorMessage = "Неверный email или пароль";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task GoToRegister()
        {
            await Shell.Current.GoToAsync("register");
        }
    }
}
