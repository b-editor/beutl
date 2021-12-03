using System.Reactive.Linq;
using System.Reactive.Subjects;

using BEditorNext.ProjectSystem;

using Reactive.Bindings;

namespace BEditorNext.Services;

public class ProjectService
{
    private readonly Subject<(Project? New, Project? Old)> _projectObservable = new();

    public ProjectService()
    {
        IsOpened = CurrentProject.Select(v => v != null).ToReadOnlyReactivePropertySlim();
    }

    public IObservable<(Project? New, Project? Old)> ProjectObservable => _projectObservable;

    public ReactivePropertySlim<Project?> CurrentProject { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsOpened { get; }

    public Project? OpenProject(string file)
    {
        try
        {
            var project = new Project();
            project.Restore(file);

            // 値を発行
            _projectObservable.OnNext((New: project, CurrentProject.Value));
            CurrentProject.Value = project;
            return project;
        }
        catch
        {
            return null;
        }
    }

    public void CloseProject()
    {
        if (CurrentProject.Value != null)
        {
            // 値を発行
            _projectObservable.OnNext((New: null, CurrentProject.Value));
            CurrentProject.Value = null;
        }
    }

    public Project? CreateProject(int width, int height, int framerate, int samplerate, string name, string location)
    {
        try
        {
            location = Path.Combine(location, name);
            var scene = new Scene(width, height, name);
            var project = new Project(framerate, samplerate)
            {
                Children =
                {
                    scene
                }
            };

            // Todo: 後で拡張子を変更
            scene.Save(Path.Combine(location, name, $"{name}.scene"));
            project.Save(Path.Combine(location, $"{name}.bep"));

            // 値を発行
            _projectObservable.OnNext((New: project, CurrentProject.Value));
            CurrentProject.Value = project;
            return project;
        }
        catch
        {
            return null;
        }
    }
}
