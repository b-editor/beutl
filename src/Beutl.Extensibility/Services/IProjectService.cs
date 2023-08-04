using Reactive.Bindings;

namespace Beutl.Extensibility.Services;

public interface IProjectService
{
    BeutlApplication Application { get; }

    IObservable<(Project? New, Project? Old)> ProjectObservable { get; }

    IReadOnlyReactiveProperty<Project?> CurrentProject { get; }

    IReadOnlyReactiveProperty<bool> IsOpened { get; }

    Project? OpenProject(string file);

    void CloseProject();

    Project? CreateProject(int width, int height, int framerate, int samplerate, string name, string location);
}
