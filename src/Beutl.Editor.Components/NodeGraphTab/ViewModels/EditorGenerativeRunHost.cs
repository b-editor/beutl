using Beutl.Editor.Services;
using Beutl.Graphics.Rendering;
using Beutl.NodeGraph.Generative;
using Microsoft.Extensions.DependencyInjection;
using Dispatcher = Avalonia.Threading.Dispatcher;

namespace Beutl.Editor.Components.NodeGraphTab.ViewModels;

/// <summary>Connects the generative runner to the editor's clock, threads and history.</summary>
internal sealed class EditorGenerativeRunHost(IEditorContext editorContext) : IGenerativeRunHost
{
    public TimeSpan CurrentTime
        => editorContext.GetService<IEditorClock>()?.CurrentTime.Value ?? TimeSpan.Zero;

    public Task<T> InvokeOnRenderThreadAsync<T>(Func<T> func)
        => RenderThread.Dispatcher.InvokeAsync(func);

    public Task InvokeOnUIThreadAsync(Action action)
        => Dispatcher.UIThread.InvokeAsync(action).GetTask();

    public void CommitHistory(string name)
        => editorContext.GetService<HistoryManager>()?.Commit(name);
}
