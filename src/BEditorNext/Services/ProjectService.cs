using System.Reactive.Linq;
using System.Xml.Linq;

using BEditorNext.ProjectItems;

using Reactive.Bindings;

namespace BEditorNext.Services;

public class ProjectService
{
    public ProjectService()
    {
        IsOpened = CurrentProject.Select(v => v != null).ToReadOnlyReactivePropertySlim();
    }

    public ReactivePropertySlim<Project?> CurrentProject { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsOpened { get; }

    public Project? OpenProject(string file)
    {
        try
        {
            var project = new Project();
            project.Restore(file);
            CurrentProject.Value = project;
            return project;
        }
        catch
        {
            return null;
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

            scene.Save(Path.Combine(location, name, $"{name}.scene"));
            project.Save(Path.Combine(location, $"{name}.bep"));
            CurrentProject.Value = project;
            return project;
        }
        catch
        {
            return null;
        }
    }
}
