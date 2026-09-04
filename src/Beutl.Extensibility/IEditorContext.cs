using Reactive.Bindings;

namespace Beutl.Extensibility;

public interface IEditorContext : IAsyncDisposable, IServiceProvider
{
    /// <summary>Gets the host close capability retained by this editor context.</summary>
    /// <remarks>
    /// Every context returned from <see cref="EditorExtension.TryCreateContext"/> must expose
    /// the capability supplied by <see cref="IEditorContextServices"/>, directly or through a
    /// context-specific wrapper. This property is the canonical close-capability access path;
    /// implementations must not duplicate it through <see cref="IServiceProvider.GetService"/>.
    /// Tool-tab extensions call it with this context and must not fall back to synchronous disposal.
    /// </remarks>
    IEditorContextCloseService CloseService { get; }

    /// <summary>Asynchronously releases the editor context and completes after all owned resources are closed.</summary>
    /// <remarks>
    /// Do not synchronously wait for disposal from a host publication or dispatcher callback. Call
    /// <see cref="CloseService"/>.<see cref="IEditorContextCloseService.RequestClose"/> instead; its
    /// completion can be observed after the callback returns. Disposal callbacks must not
    /// synchronously start and wait for another project or editor lifecycle operation on any thread;
    /// enqueue that work to begin after the callback returns.
    /// </remarks>
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
