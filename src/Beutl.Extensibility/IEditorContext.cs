using Reactive.Bindings;

namespace Beutl.Extensibility;

/// <summary>Provides the document, tool tabs, and lifetime of an editor.</summary>
/// <remarks>
/// Implement <see cref="ISavableEditorContext"/> and/or <see cref="IUndoRedoEditorContext"/>
/// on the context to opt into the host's save and history commands.
/// </remarks>
public interface IEditorContext : IDisposable, IAsyncDisposable, IServiceProvider
{
    CoreObject Object { get; }

    EditorExtension Extension { get; }

    IReactiveProperty<bool> IsEnabled { get; }

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
