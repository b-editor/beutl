using BeUtl.ProjectSystem;
using BeUtl.Services;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace BeUtl.ViewModels;

public sealed class EditPageViewModel
{
    private readonly ProjectService _projectService;

    public EditPageViewModel()
    {
        _projectService = ServiceLocator.Current.GetRequiredService<ProjectService>();
        _projectService.CurrentProject.Subscribe(i =>
        {

        });
    }

    public ReactivePropertySlim<Project?> Project => _projectService.CurrentProject;

    public ReadOnlyReactivePropertySlim<bool> IsProjectOpened => _projectService.IsOpened;
}
