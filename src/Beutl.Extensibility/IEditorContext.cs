using Reactive.Bindings;

namespace Beutl.Extensibility;

public interface IEditorContext : IDisposable, IServiceProvider
{
    EditorExtension Extension { get; }

    string EdittingFile { get; }

    IReactiveProperty<bool> IsEnabled { get; }

    IKnownEditorCommands? Commands { get; }

    T? FindToolTab<T>(Func<T, bool> condition)
        where T : IToolContext;

    T? FindToolTab<T>()
        where T : IToolContext;

    bool OpenToolTab(IToolContext item);

    void CloseToolTab(IToolContext item);
}
