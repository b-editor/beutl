using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Services;

public sealed class EditorTabItem : IAsyncDisposable
{
    public EditorTabItem(IEditorContext context)
    {
        Context = new ReactiveProperty<IEditorContext>(context);
        FilePath = Context.Select(ctxt => ctxt?.Object.Uri?.LocalPath)
            .ToReadOnlyReactivePropertySlim()!;
        FileName = FilePath.Select(Path.GetFileName)
            .ToReadOnlyReactivePropertySlim()!;
        Extension = Context.Select(ctxt => ctxt?.Extension!)
            .ToReadOnlyReactivePropertySlim()!;
    }

    public IReactiveProperty<IEditorContext> Context { get; }

    public IReadOnlyReactiveProperty<string> FilePath { get; }

    public IReadOnlyReactiveProperty<string> FileName { get; }

    public IReadOnlyReactiveProperty<EditorExtension> Extension { get; }

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public async ValueTask DisposeAsync()
    {
        Exception? firstFailure = null;
        void Cleanup(Action action)
        {
            try { action(); }
            catch (Exception error)
            {
                if (firstFailure is null)
                    firstFailure = error;
                else
                {
                    try { Log.CreateLogger<EditorTabItem>().LogWarning(error, "An additional editor tab cleanup step failed."); }
                    catch { } // Preserve the original context disposal error.
                }
            }
        }

        try
        {
            await Context.Value.DisposeAsync();
        }
        catch (Exception error)
        {
            firstFailure = error;
        }
        finally
        {
            // A removed tab must release its context and subscriptions even when the context
            // finishes teardown by reporting a persistence failure.
            Cleanup(() => Context.Value = null!);
            Cleanup(Context.Dispose);
            Cleanup(FilePath.Dispose);
            Cleanup(FileName.Dispose);
            Cleanup(Extension.Dispose);
            Cleanup(IsSelected.Dispose);
        }

        if (firstFailure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(firstFailure).Throw();
    }
}
