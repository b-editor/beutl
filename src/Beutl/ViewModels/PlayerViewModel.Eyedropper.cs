using Avalonia.Threading;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.Models;
using Reactive.Bindings;
using BtlPoint = Beutl.Graphics.Point;

namespace Beutl.ViewModels;

public partial class PlayerViewModel : IPreviewEyedropper
{
    // The click the preview waits for, which PlayerView gives it.
    internal ReactivePropertySlim<PreviewPointPick?> PointPick { get; } = new();

    Task<BtlPoint?> IPreviewEyedropper.PickPointAsync(Action<BtlPoint?> hovered, CancellationToken cancellationToken)
    {
        StopPuppetPinPlacement();
        return StartPointPick(new PreviewPointPick(hovered), cancellationToken);
    }

    private Task<BtlPoint?> StartPointPick(PreviewPointPick pick, CancellationToken cancellationToken)
    {
        PointPick.Value?.Cancel();
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
    async Task<(Bitmap Frame, float Scale)?> IPreviewEyedropper.DrawFrameReceivedBy(
        FilterEffect effect, CancellationToken cancellationToken)
    {
        await Pause();
        cancellationToken.ThrowIfCancellationRequested();
        float scale = EditViewModel.Renderer.Value.OutputScale;
        HashSet<EngineObject> after = FilterEffectOrder.After(effect);

        return await RenderThread.Dispatcher.InvokeAsync<(Bitmap Frame, float Scale)?>(() =>
        {
            if (Scene is not { } scene)
                return null;

            cancellationToken.ThrowIfCancellationRequested();
            using SceneRenderer renderer = EditViewModel.CreatePreviewRenderer(scene, scale, disableResourceShare: true);
            renderer.CacheOptions = RenderCacheOptions.Disabled;
            renderer.Compositor.DisabledObjects = new HashSet<EngineObject> { effect };
            renderer.Compositor.ColorStagesSkipped = after;
            renderer.Render(renderer.Compositor.EvaluateGraphics(CurrentFrame.Value, GetFrameRate(), cancellationToken));
            return (renderer.Snapshot(), scale);
        }, ct: cancellationToken);
    }
}

// A click on the frame that a tool tab waits for; see IPreviewEyedropper.PickPointAsync.
internal sealed class PreviewPointPick(Action<BtlPoint?> hovered, CoreObject? localOwner = null,
    Func<BtlPoint, bool>? accepts = null)
{
    private readonly TaskCompletionSource<BtlPoint?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<BtlPoint?> Task => _result.Task;

    public CoreObject? LocalOwner { get; } = localOwner;

    public event Action? Ended;

    public void Hover(BtlPoint? point)
    {
        if (!_result.Task.IsCompleted)
            hovered(point);
    }

    public void Complete(BtlPoint point)
    {
        if (accepts?.Invoke(point) != false)
            End(point);
    }

    public void Cancel() => End(null);

    private void End(BtlPoint? point)
    {
        if (_result.TrySetResult(point))
            Ended?.Invoke();
    }
}
