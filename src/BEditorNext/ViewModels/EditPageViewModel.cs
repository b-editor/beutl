using BEditorNext.ProjectItems;
using BEditorNext.Services;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace BEditorNext.ViewModels;

public sealed class EditPageViewModel
{
    private readonly ProjectService _projectService;

    public EditPageViewModel()
    {
        _projectService = ServiceLocator.Current.GetRequiredService<ProjectService>();
    }

    public ReactivePropertySlim<Project?> Project => _projectService.CurrentProject;

    public ReadOnlyReactivePropertySlim<bool> IsProjectOpened => _projectService.IsOpened;
}
