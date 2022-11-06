using Reactive.Bindings;

namespace Beutl.Framework.Services;

public interface IProjectService
{
    IObservable<(IWorkspace? New, IWorkspace? Old)> ProjectObservable { get; }

    IReactiveProperty<IWorkspace?> CurrentProject { get; }

    IReadOnlyReactiveProperty<bool> IsOpened { get; }

    IWorkspace? OpenProject(string file);

    void CloseProject();

    IWorkspace? CreateProject(int width, int height, int framerate, int samplerate, string name, string location);
}
