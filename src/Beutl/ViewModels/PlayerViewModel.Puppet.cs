using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Threading;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Models;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels;

public partial class PlayerViewModel : IPreviewPuppetPinEditor
{
    private CancellationTokenSource? _puppetPlacement;
    private readonly ReactivePropertySlim<Ref<Bitmap>?> _puppetPlacementImage = new();

    internal ReactivePropertySlim<Puppet?> PuppetPlacementEffect { get; } = new();

    internal ReadOnlyReactivePropertySlim<Ref<Bitmap>?> EditorPreviewImage { get; private set; } = null!;

    internal ReactivePropertySlim<PuppetPin?> SelectedPuppetPin { get; } = new();

    IReadOnlyReactiveProperty<PuppetPin?> IPreviewPuppetPinEditor.SelectedPin => SelectedPuppetPin;

    private void InitializePuppetPinEditor()
    {
        EditorPreviewImage = _puppetPlacementImage.CombineLatest(PreviewImage, static (placement, preview) => placement ?? preview)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        _puppetPlacementImage.DisposeWith(_disposables);
        _editorSelection.SelectedObject.Subscribe(selected =>
        {
            if (SelectedPuppetPin.Value is { } pin && !ReferenceEquals(pin.FindHierarchicalParent<Puppet>(), selected))
                SetSelectedPuppetPin(null);
        }).DisposeWith(_disposables);
        SelectedPuppetPin.DisposeWith(_disposables);
        PuppetPlacementEffect.DisposeWith(_disposables);
    }

    void IPreviewPuppetPinEditor.SelectPin(Puppet effect, PuppetPin pin)
    {
        StopPuppetPinPlacement();
        SelectPuppetPin(effect, pin);
    }

    internal void SelectPuppetPin(Puppet effect, PuppetPin pin)
    {
        if (!effect.Pins.Contains(pin)) return;

        ActivatePuppetPinEditing(effect);
        SetSelectedPuppetPin(pin);
    }

    private void ActivatePuppetPinEditing(Puppet effect)
    {
        PathEditor.FigureContext.Value = null;
        IsMoveMode.Value = true;
        if (!ReferenceEquals(_editorSelection.SelectedObject.Value, effect))
            _editorSelection.Select(effect);
    }

    private void SetSelectedPuppetPin(PuppetPin? pin)
    {
        PuppetPin? old = SelectedPuppetPin.Value;
        if (ReferenceEquals(old, pin)) return;

        if (old is not null) old.DetachedFromHierarchy -= OnSelectedPinDetached;
        if (pin is not null) pin.DetachedFromHierarchy += OnSelectedPinDetached;
        SelectedPuppetPin.Value = pin;
    }

    private void OnSelectedPinDetached(object? sender, HierarchyAttachmentEventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess()) ClearSelection();
        else Dispatcher.UIThread.Post(ClearSelection);

        void ClearSelection()
        {
            if (ReferenceEquals(SelectedPuppetPin.Value, sender))
                SetSelectedPuppetPin(null);
        }
    }

    internal void StopPuppetPinPlacement()
    {
        CancellationTokenSource? request = _puppetPlacement;
        if (request is null) return;

        ClearPuppetPinPlacement();
        request.Cancel();
    }

    private void ClearPuppetPinPlacement()
    {
        _puppetPlacement = null;
        ReplaceImage(_puppetPlacementImage, null);
        PuppetPlacementEffect.Value = null;
    }

    async Task<bool> IPreviewPuppetPinEditor.PlacePinsAsync(
        Puppet effect, Func<PuppetPin, bool> addPin, CancellationToken cancellationToken)
    {
        if (_isDisposing || cancellationToken.IsCancellationRequested) return true;

        StopPuppetPinPlacement();
        PointPick.Value?.Cancel();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _puppetPlacement = request;
        ActivatePuppetPinEditing(effect);
        PuppetPlacementEffect.Value = effect;
        using var select = _editorSelection.SelectedObject.Where(selected => !ReferenceEquals(selected, effect))
            .Subscribe(_ => request.Cancel());
        using var tool = IsMoveMode.Where(move => !move).Subscribe(_ => request.Cancel());
        try
        {
            await Pause();
            if (request.IsCancellationRequested) return true;
            if (Scene is not { } scene) return false;

            MediaTime time = CurrentFrame.Value;
            Rational frameRate = GetFrameRate();
            float scale = EditViewModel.Renderer.Value.OutputScale;
            using var seek = CurrentFrame.Where(frame => frame != time).Subscribe(_ => request.Cancel());
            using var playback = IsPlaying.Where(static playing => playing).Subscribe(_ => request.Cancel());
            int sceneRevision = 0;
            int pickRevision = 0;
            PreviewPointPick? livePick = null;
            scene.Edited += OnSceneEdit;
            using var edits = Disposable.Create(() => scene.Edited -= OnSceneEdit);
            while (!request.IsCancellationRequested)
            {
                int revision = Volatile.Read(ref sceneRevision);
                HashSet<EngineObject> after = FilterEffectOrder.After(effect);
                var capture = await RenderThread.Dispatcher.InvokeAsync(() =>
                {
                    using SceneRenderer renderer = EditViewModel.CreatePreviewRenderer(scene, scale, disableResourceShare: true);
                    renderer.CacheOptions = RenderCacheOptions.Disabled;
                    // Show the same effect stage that supplies the placement mesh. Later effects may move its pixels.
                    renderer.Compositor.DisabledObjects = after;
                    Puppet.PinPlacement? capturedPlacement = effect.CapturePinPlacement(() => renderer.Render(
                        renderer.Compositor.EvaluateGraphics(time, frameRate, request.Token)));
                    var stageImage = after.Count > 0 && capturedPlacement is not null ? Ref<Bitmap>.Create(renderer.Snapshot()) : null;
                    return (Placement: capturedPlacement, Image: stageImage);
                }, ct: request.Token);
                using var image = capture.Image;
                if (request.IsCancellationRequested) return true;
                if (revision != Volatile.Read(ref sceneRevision)) continue;
                if (capture.Placement is not { } placement) return false;

                // Refresh the transform after the isolated renderer has released the render thread.
                ReplaceImage(_puppetPlacementImage, image);
                _editorSelection.Select(effect, notifyIfSelected: true);
                PuppetPin? pin = null;
                pickRevision = revision;
                livePick = new PreviewPointPick(_ => { }, effect, point => (pin = placement.CreatePin(point)) is not null);
                await StartPointPick(livePick, request.Token);
                livePick = null;
                if (request.IsCancellationRequested) return true;
                if (revision != Volatile.Read(ref sceneRevision)) continue;
                if (pin is null || !addPin(pin)) return true;

                SelectPuppetPin(effect, pin);
            }
            return true;

            // Undo and property edits can change the silhouette or mesh while the preview waits for a click.
            // Discard just that captured frame and keep the placement session open.
            void OnSceneEdit(object? sender, EventArgs e)
            {
                Interlocked.Increment(ref sceneRevision);
                if (Dispatcher.UIThread.CheckAccess()) RefreshPick();
                else Dispatcher.UIThread.Post(RefreshPick);
            }

            void RefreshPick()
            {
                if (pickRevision != Volatile.Read(ref sceneRevision))
                    livePick?.Cancel();
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            return true;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Could not prepare the Puppet pin placement preview.");
            return false;
        }
        finally
        {
            if (ReferenceEquals(_puppetPlacement, request))
                ClearPuppetPinPlacement();
        }
    }
}
