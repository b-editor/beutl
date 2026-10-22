using Beutl.Editor.Services;
using Beutl.Graphics.Rendering;
using Beutl.NodeGraph.Generative;
using Microsoft.Extensions.DependencyInjection;
using Dispatcher = Avalonia.Threading.Dispatcher;

namespace Beutl.Editor.Components.NodeGraphTab.ViewModels;

/// <summary>Connects the generative runner to the editor's clock, threads and history.</summary>
internal sealed class EditorGenerativeRunHost(IEditorContext editorContext) : IGenerativeRunHost
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

    public void CommitHistory(string name)
        => editorContext.GetService<HistoryManager>()?.Commit(name);
}
