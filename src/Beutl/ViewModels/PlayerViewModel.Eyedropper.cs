using Avalonia.Threading;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.Models;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using BtlPoint = Beutl.Graphics.Point;

namespace Beutl.ViewModels;

public partial class PlayerViewModel : IPreviewEyedropper
{
    // The click the preview waits for, which PlayerView gives it.
    internal ReactivePropertySlim<PreviewPointPick?> PointPick { get; } = new();

    Task<BtlPoint?> IPreviewEyedropper.PickPointAsync(Action<BtlPoint?> hovered, CancellationToken cancellationToken)
    {
        PointPick.Value?.Cancel();
        var pick = new PreviewPointPick(hovered);
        CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            if (Dispatcher.UIThread.CheckAccess())
                pick.Cancel();
            else
                Dispatcher.UIThread.Post(pick.Cancel);
        });
        pick.Ended += () =>
        {
            registration.Dispose();
            if (PointPick.Value == pick)
                PointPick.Value = null;
        };
        if (!pick.Task.IsCompleted)
            PointPick.Value = pick;
        return pick.Task;
    }

    // A throwaway renderer with the preview's scale, so the preview's own resources and caches stay as they are.
    async Task<(Bitmap Frame, float Scale)?> IPreviewEyedropper.DrawFrameWithout(
        EngineObject excluded, CancellationToken cancellationToken)
    {
        await Pause();
        cancellationToken.ThrowIfCancellationRequested();
        float scale = EditViewModel.Renderer.Value.OutputScale;
        // The preview's settings, so the pick reads the media the preview shows, original or proxy.
        GlobalConfiguration configuration = EditViewModel.GetRequiredService<GlobalConfiguration>();
        Beutl.Media.FontManager fonts = EditViewModel.GetRequiredService<Beutl.Media.FontManager>();

        return await RenderThread.Dispatcher.InvokeAsync<(Bitmap Frame, float Scale)?>(() =>
        {
            if (Scene is not { } scene)
                return null;

            cancellationToken.ThrowIfCancellationRequested();
            using var renderer = new SceneRenderer(
                scene,
                fonts,
                RenderIntent.Preview,
                scale,
                disableResourceShare: true,
                maxWorkingScale: WorkingScaleCeiling.Preview(scale),
                editorConfig: configuration.EditorConfig,
                proxyConfig: configuration.ProxyStoreConfig);
            renderer.CacheOptions = RenderCacheOptions.Disabled;
            renderer.Compositor.DisabledObjects = new HashSet<EngineObject> { excluded };
            renderer.Render(renderer.Compositor.EvaluateGraphics(CurrentFrame.Value, GetFrameRate(), cancellationToken));
            return (renderer.Snapshot(), scale);
        }, ct: cancellationToken);
    }
}

// A click on the frame that a tool tab waits for; see IPreviewEyedropper.PickPointAsync.
internal sealed class PreviewPointPick(Action<BtlPoint?> hovered)
{
    private readonly TaskCompletionSource<BtlPoint?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<BtlPoint?> Task => _result.Task;

    public event Action? Ended;

    public void Hover(BtlPoint? point)
    {
        if (!_result.Task.IsCompleted)
            hovered(point);
    }

    public void Complete(BtlPoint point) => End(point);

    public void Cancel() => End(null);

    private void End(BtlPoint? point)
    {
        if (_result.TrySetResult(point))
            Ended?.Invoke();
    }
}
