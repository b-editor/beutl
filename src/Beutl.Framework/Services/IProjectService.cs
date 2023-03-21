using Reactive.Bindings;

namespace Beutl.Framework.Services;

public interface IProjectService
{
    IObservable<(Project? New, Project? Old)> ProjectObservable { get; }

    IReactiveProperty<Project?> CurrentProject { get; }

    IReadOnlyReactiveProperty<bool> IsOpened { get; }

    Project? OpenProject(string file);

    void CloseProject();

    Project? CreateProject(int width, int height, int framerate, int samplerate, string name, string location);
}
