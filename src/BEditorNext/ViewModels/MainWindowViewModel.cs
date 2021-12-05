using System;
using System.Collections.Generic;
using System.Text;

using BEditorNext.ProjectSystem;
using BEditorNext.Services;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace BEditorNext.ViewModels;

public class MainWindowViewModel
{
    private readonly ProjectService _projectService;

    public MainWindowViewModel()
    {
        _projectService = ServiceLocator.Current.GetRequiredService<ProjectService>();

        IsProjectOpened = _projectService.IsOpened;
        Undo = new(_projectService.IsOpened);
        Redo = new(_projectService.IsOpened);
        Save = new(_projectService.IsOpened);
        SaveAll = new(_projectService.IsOpened);
        Undo.Subscribe(() => CommandRecorder.Default.Undo());
        Redo.Subscribe(() => CommandRecorder.Default.Redo());

        SaveAll.Subscribe(() =>
        {
            Project? project = _projectService.CurrentProject.Value;
            if (project != null)
            {
                project.Save(project.FileName);

                foreach (Scene scene in project.Scenes)
                {
                    scene.Save(scene.FileName);
                    foreach (SceneLayer layer in scene.Layers)
                    {
                        layer.Save(layer.FileName);
                    }
                }
            }
        });
    }

    public ReactiveCommand Undo { get; }

    public ReactiveCommand Redo { get; }

    public ReactiveCommand Save { get; }

    public ReactiveCommand SaveAll { get; }

    public ReadOnlyReactivePropertySlim<bool> IsProjectOpened { get; }
}
