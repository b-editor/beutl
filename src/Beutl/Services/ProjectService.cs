using System.Reactive.Subjects;

using Beutl.Configuration;
using Beutl.Framework;
using Beutl.Framework.Services;
using Beutl.Models;
using Beutl.ProjectSystem;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace Beutl.Services;

public sealed class ProjectService : IProjectService
{
    private readonly Subject<(IWorkspace? New, IWorkspace? Old)> _projectObservable = new();
    private readonly ReactivePropertySlim<IWorkspace?> _currentProject = new();
    private readonly ReadOnlyReactivePropertySlim<bool> _isOpened;

    public ProjectService()
    {
        _isOpened = CurrentProject.Select(v => v != null).ToReadOnlyReactivePropertySlim();
    }

    public IObservable<(IWorkspace? New, IWorkspace? Old)> ProjectObservable => _projectObservable;

    public IReactiveProperty<IWorkspace?> CurrentProject => _currentProject;

    public IReadOnlyReactiveProperty<bool> IsOpened => _isOpened;

    public IWorkspace? OpenProject(string file)
    {
        try
        {
            var project = new Project();
            project.Restore(file);

            IWorkspace? old = CurrentProject.Value;
            CurrentProject.Value = project;
            // 値を発行
            _projectObservable.OnNext((New: project, old));

            AddToRecentProjects(file);

            return project;
        }
        catch
        {
            Debug.Fail("Unable to open the project.");
            return null;
        }
    }

    public void CloseProject()
    {
        if (CurrentProject.Value is { } project)
        {
            // 値を発行
            _projectObservable.OnNext((New: null, project));
            CurrentProject.Value = null;
            project.Dispose();
        }
    }

    public IWorkspace? CreateProject(int width, int height, int framerate, int samplerate, string name, string location)
    {
        try
        {
            location = Path.Combine(location, name);
            IWorkspaceItemContainer container = ServiceLocator.Current.GetRequiredService<IWorkspaceItemContainer>();
            var scene = new Scene(width, height, name);
            container.Add(scene);
            var project = new Project()
            {
                Items =
                {
                    scene
                },
                Variables =
                {
                    [ProjectVariableKeys.FrameRate] = framerate.ToString(),
                    [ProjectVariableKeys.SampleRate] = samplerate.ToString(),
                }
            };

            scene.Save(Path.Combine(location, name, $"{name}.{Constants.SceneFileExtension}"));
            string projectFile = Path.Combine(location, $"{name}.{Constants.ProjectFileExtension}");
            project.Save(projectFile);

            // 値を発行
            _projectObservable.OnNext((New: project, CurrentProject.Value));
            CurrentProject.Value = project;

            AddToRecentProjects(projectFile);

            return project;
        }
        catch
        {
            return null;
        }
    }

    private static void AddToRecentProjects(string file)
    {
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        viewConfig.UpdateRecentProject(file);
        viewConfig.UpdateRecentFile(file);
    }
}
