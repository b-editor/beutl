using Beutl.Editor.Services;
using Beutl.Graphics.Rendering;
using Beutl.NodeGraph.Generative;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Dispatcher = Avalonia.Threading.Dispatcher;

namespace Beutl.Editor.Components.NodeGraphTab.ViewModels;

/// <summary>Connects the generative runner to the editor's clock, threads and history.</summary>
internal sealed class EditorGenerativeRunHost(IEditorContext editorContext, IReadOnlyReactiveProperty<bool>? canEdit = null)
    : IGenerativeRunHost
{
    public MediaTime CurrentTime
        => editorContext.GetService<IEditorClock>()?.CurrentTime.Value ?? MediaTime.Zero;

    public Task<T> InvokeOnRenderThreadAsync<T>(Func<T> func)
        => RenderThread.Dispatcher.InvokeAsync(func);

    // A result that arrives during an edit session waits for it, so it never lands in the gesture's entry.
    public Task InvokeOnUIThreadAsync(Action action)
        => Dispatcher.UIThread.InvokeAsync(() => editorContext.GetService<HistoryManager>() is { } history
            ? history.RunAfterEditAsync(action)
            : RunNow(action));

    private static Task RunNow(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    // A result that arrives after the clip was locked is dropped, so the locked clip keeps what it had.
    public void CommitHistory(string name)
    {
        if (editorContext.GetService<HistoryManager>() is not { } history) return;
        if (canEdit is { Value: false }) history.Rollback();
        else history.Commit(name);
    }
}
