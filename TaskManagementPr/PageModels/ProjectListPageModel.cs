using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskManagementPr.Data;
using TaskManagementPr.Models;
using TaskManagementPr.Services;

namespace TaskManagementPr.PageModels
{
    public partial class ProjectListPageModel : ObservableObject
    {
        private readonly ProjectRepository _projectRepository;
        private readonly ProjectMemberRepository _projectMemberRepository;
        private readonly IAuthService _authService;

        [ObservableProperty]
        private List<Project> _projects = [];

        [ObservableProperty]
        private Project? selectedProject;

        public ProjectListPageModel(
            ProjectRepository projectRepository,
            ProjectMemberRepository projectMemberRepository,
            IAuthService authService)
        {
            _projectRepository = projectRepository;
            _projectMemberRepository = projectMemberRepository;
            _authService = authService;
        }

        [RelayCommand]
        private async Task Appearing()
        {
            var realEmail = await _authService.GetEmailAsync();
            await _projectMemberRepository.ActivatePendingForEmailAsync(realEmail);
            Projects = await _projectRepository.ListAsync();
        }

        [RelayCommand]
        Task? NavigateToProject(Project project)
            => project is null ? Task.CompletedTask : Shell.Current.GoToAsync($"project?id={project.ID}");

        [RelayCommand]
        async Task AddProject()
        {
            await Shell.Current.GoToAsync($"project");
        }
    }
}