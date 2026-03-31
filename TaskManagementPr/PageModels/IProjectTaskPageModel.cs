using CommunityToolkit.Mvvm.Input;
using TaskManagementPr.Models;

namespace TaskManagementPr.PageModels
{
    public interface IProjectTaskPageModel
    {
        IAsyncRelayCommand<ProjectTask> NavigateToTaskCommand { get; }
        bool IsBusy { get; }
    }
}