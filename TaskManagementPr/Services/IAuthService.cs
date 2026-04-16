namespace TaskManagementPr.Services
{
    public interface IAuthService
    {
        bool IsAuthenticated { get; }
        string? CurrentUserEmail { get; }
        Task<string?> GetEmailAsync();
        Task<bool> SignInAsync(string email, string password);
        Task<bool> SignUpAsync(string email, string password);
        Task SignOutAsync();
    }
}
