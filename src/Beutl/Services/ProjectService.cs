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
    private readonly Subject<(Project? New, Project? Old)> _projectObservable = new();
    private readonly ReadOnlyReactivePropertySlim<bool> _isOpened;

    public ProjectService()
    {
        CurrentProject = Application.GetObservable(BeutlApplication.ProjectProperty)
            .ToReadOnlyReactivePropertySlim();
        _isOpened = CurrentProject.Select(v => v != null).ToReadOnlyReactivePropertySlim();
    }

    public BeutlApplication Application { get; } = ServiceLocator.Current.GetRequiredService<BeutlApplication>();

    public IObservable<(Project? New, Project? Old)> ProjectObservable => _projectObservable;

    public IReadOnlyReactiveProperty<Project?> CurrentProject { get; }

    public IReadOnlyReactiveProperty<bool> IsOpened => _isOpened;

    public Project? OpenProject(string file)
    {
        try
        {
            CommandRecorder.Default.Clear();
            var project = new Project();
            project.Restore(file);

            Project? old = Application.Project;
            Application.Project = project;
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
        if (Application.Project is { } project)
        {
            CommandRecorder.Default.Clear();
            // 値を発行
            _projectObservable.OnNext((New: null, project));
            project.Dispose();
            Application.Project = null;
        }
    }

    public Project? CreateProject(int width, int height, int framerate, int samplerate, string name, string location)
    {
        try
        {
            CommandRecorder.Default.Clear();
            location = Path.Combine(location, name);
            IProjectItemContainer container = ServiceLocator.Current.GetRequiredService<IProjectItemContainer>();
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
            _projectObservable.OnNext((New: project, Application.Project));
            Application.Project = project;

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
