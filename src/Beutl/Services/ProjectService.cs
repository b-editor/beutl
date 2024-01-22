using System.Reactive.Subjects;

using Beutl.Configuration;
using Beutl.Logging;
using Beutl.Models;
using Beutl.ProjectSystem;

using Microsoft.Extensions.Logging;

using OpenTelemetry.Trace;

using Reactive.Bindings;

namespace Beutl.Services;

public sealed class ProjectService
{
    private readonly Subject<(Project? New, Project? Old)> _projectObservable = new();
    private readonly ReadOnlyReactivePropertySlim<bool> _isOpened;
    private readonly BeutlApplication _app = BeutlApplication.Current;
    private readonly ILogger _logger = Log.CreateLogger<ProjectService>();

    public ProjectService()
    {
        CurrentProject = _app.GetObservable(BeutlApplication.ProjectProperty)
            .ToReadOnlyReactivePropertySlim();
        _isOpened = CurrentProject.Select(v => v != null).ToReadOnlyReactivePropertySlim();
    }

    public static ProjectService Current { get; } = new();

    public IObservable<(Project? New, Project? Old)> ProjectObservable => _projectObservable;

    public IReadOnlyReactiveProperty<Project?> CurrentProject { get; }

    public IReadOnlyReactiveProperty<bool> IsOpened => _isOpened;

    public Project? OpenProject(string file)
    {
        using Activity? activity = Telemetry.StartActivity("OpenProject");
        try
        {
            var project = new Project();
            project.Restore(file);

            Project? old = _app.Project;
            _app.Project = project;
            // 値を発行
            _projectObservable.OnNext((New: project, old));

            AddToRecentProjects(file);

            return project;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            _logger.LogError(ex, "Unable to open the project.");
            return null;
        }
    }

    public void CloseProject()
    {
        if (_app.Project is { } project)
        {
            // 値を発行
            _projectObservable.OnNext((New: null, project));
            project.Dispose();
            _app.Project = null;
        }
    }

    public Project? CreateProject(int width, int height, int framerate, int samplerate, string name, string location)
    {
        using Activity? activity = Telemetry.StartActivity("CreateProject");
        activity?.SetTag(nameof(width), width);
        activity?.SetTag(nameof(height), height);
        activity?.SetTag(nameof(framerate), framerate);
        activity?.SetTag(nameof(samplerate), samplerate);
        try
        {
            location = Path.Combine(location, name);
            var scene = new Scene(width, height, name);
            ProjectItemContainer.Current.Add(scene);
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
            _projectObservable.OnNext((New: project, _app.Project));
            _app.Project = project;

            AddToRecentProjects(projectFile);

            return project;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            _logger.LogError(ex, "Unable to open the project.");
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
