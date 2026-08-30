using Reactive.Bindings;

namespace Beutl.Extensibility;

public interface IEditorContext : IAsyncDisposable, IServiceProvider
{
    /// <summary>Asynchronously releases the editor context and completes after all owned resources are closed.</summary>
    new ValueTask DisposeAsync();

    CoreObject Object { get; }

    EditorExtension Extension { get; }

    IReactiveProperty<bool> IsEnabled { get; }

    IKnownEditorCommands? Commands { get; }

    T? FindToolTab<T>(Func<T, bool> condition)
        where T : IToolContext;

    T? FindToolTab<T>()
        where T : IToolContext;

    /// <summary>Transfers ownership of a tool context to the editor host.</summary>
    /// <returns>
    /// <see langword="true"/> when the tab was opened or activated; otherwise
    /// <see langword="false"/> after the supplied context has been disposed.
    /// </returns>
    /// <remarks>The supplied context is consumed for both return values and must not be reused.</remarks>
    ValueTask<bool> OpenToolTabAsync(IToolContext item);

    /// <summary>Closes a host-owned tool tab and completes after its asynchronous teardown has finished.</summary>
    /// <remarks>
    /// A close reentered from an <see cref="IToolContext.DisposeAsync"/> callback is scheduled and
    /// returns without awaiting a sibling callback, so mutually closing tools cannot deadlock. The
    /// enclosing host layout or editor teardown joins every scheduled close before it completes.
    /// </remarks>
    ValueTask CloseToolTabAsync(IToolContext item);
}
