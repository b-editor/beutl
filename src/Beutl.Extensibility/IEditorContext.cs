using Reactive.Bindings;

namespace Beutl.Extensibility;

public interface IEditorContext : IDisposable, IAsyncDisposable, IServiceProvider
{
    CoreObject Object { get; }

    EditorExtension Extension { get; }

    IReactiveProperty<bool> IsEnabled { get; }

    IKnownEditorCommands? Commands { get; }

    T? FindToolTab<T>(Func<T, bool> condition)
        where T : IToolContext;

    T? FindToolTab<T>()
        where T : IToolContext;

    bool OpenToolTab(IToolContext item);

    void CloseToolTab(IToolContext item);

    void IDisposable.Dispose()
    {
    }

    ValueTask IAsyncDisposable.DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
