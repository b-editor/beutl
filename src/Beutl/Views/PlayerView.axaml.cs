using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Threading;

using Beutl.Composition;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PathEditorTab.ViewModels;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Logging;
using Beutl.ProjectSystem;
using Beutl.ViewModels;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Reactive.Bindings.Extensions;

using BtlDrawable = Beutl.Graphics.Drawable;
using BtlMatrix = Beutl.Graphics.Matrix;
using BtlPoint = Beutl.Graphics.Point;
using BtlRect = Beutl.Graphics.Rect;
using BtlSize = Beutl.Graphics.Size;

namespace Beutl.Views;

public partial class PlayerView : UserControl
{
    private readonly CompositeDisposable _disposables = [];
    private readonly ILogger _logger = Log.CreateLogger<PlayerView>();
    private IDisposable? _imageConfigSubscription;
    private IDisposable? _boundsSubscription;
    internal Control image = null!;

    // _transformHandleResource is RenderThread-owned: only mutate inside RenderThread.Dispatcher.
    private BtlDrawable.Resource? _transformHandleResource;
    private BtlDrawable? _transformHandleResourceTarget;

    public PlayerView()
    {
        InitializeComponent();

        SetupImageControl();

        ConfigureFrameContextMenu(framePanel);
        framePanel.PointerPressed += OnFramePointerPressed;
        framePanel.PointerReleased += OnFramePointerReleased;
        framePanel.PointerMoved += OnFramePointerMoved;
        framePanel.PointerCaptureLost += OnFramePointerCaptureLost;
        framePanel.AddHandler(PointerWheelChangedEvent, OnFramePointerWheelChanged, RoutingStrategies.Tunnel);

        framePanel.Focusable = true;
        framePanel.KeyDown += OnFrameKeyDown;
        framePanel.KeyUp += OnFrameKeyUp;

        framePanel.GetObservable(BoundsProperty)
            .Subscribe(_ => UpdateMaxFrameSize());

        // PlayerView.axaxml.DragAndDrop.cs
        DragDrop.SetAllowDrop(framePanel, true);
        framePanel.AddHandler(DragDrop.DragOverEvent, OnFrameDragOver);
        framePanel.AddHandler(DragDrop.DropEvent, OnFrameDrop);

        Player.CurrentTimeSubmitted += OnPlayerCurrentTimeSubmitted;
    }

    private async void OnPlayerCurrentTimeSubmitted(object? sender, Beutl.Controls.TimecodeSubmittedEventArgs e)
    {
        if (DataContext is not PlayerViewModel vm)
        {
            // A wrong DataContext on a PlayerView event handler is a wiring bug,
            // not a user error — log at Error level so it surfaces in telemetry,
            // and show a generic "unexpected error" message instead of the
            // misleading "Invalid timecode format".
            _logger.LogError("Timecode submitted but DataContext is not PlayerViewModel; ignoring.");
            e.Reject(Beutl.Language.MessageStrings.UnexpectedError);
            return;
        }

        // Parse synchronously so Player.SubmitCurrentTimeEdit sees the final
        // Handled/Error state before its post-Invoke check. The actual seek
        // is applied after awaiting Pause if playback is running, otherwise
        // the playback timer would overwrite the new playhead on its next tick.
        if (!vm.TryParseTimecode(e.Input, out TimeSpan target, out GotoTimecodeError error))
        {
            e.Reject(LookupLocalizedError(error));
            return;
        }

        e.Accept();
        try
        {
            if (vm.IsPlaying.Value)
            {
                await vm.Pause();
            }
            vm.ApplyTimecodeSeek(target);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while applying timecode '{Input}'.", e.Input);
        }
    }

    private string LookupLocalizedError(GotoTimecodeError error) => error switch
    {
        GotoTimecodeError.InvalidFormat => Beutl.Language.Strings.GotoTimecode_InvalidFormat,
        GotoTimecodeError.MarkerNotFound => Beutl.Language.Strings.GotoTimecode_MarkerNotFound,
        GotoTimecodeError.NoScene => Beutl.Language.Strings.GotoTimecode_NoScene,
        GotoTimecodeError.OutOfRange => Beutl.Language.Strings.GotoTimecode_OutOfRange,
        _ => Beutl.Language.Strings.GotoTimecode_InvalidFormat,
    };

    private void SetupImageControl()
    {
        var config = GlobalConfiguration.Instance.EditorConfig;
        SwapImageControl(config.UseHdrPreview);

        _imageConfigSubscription = config.GetObservable(EditorConfig.UseHdrPreviewProperty)
            .Skip(1)
            .Subscribe(useHdr => Dispatcher.UIThread.InvokeAsync(() => SwapImageControl(useHdr)));
    }

    private void SwapImageControl(bool useHdr)
    {
        if (image != null)
        {
            framePanel.Children.Remove(image);
        }

        _boundsSubscription?.Dispose();

        Control newImage;
        if (useHdr)
        {
            var hdr = new HdrBitmapView();
            hdr.Bind(HdrBitmapView.SourceProperty, new Binding("PreviewImage.Value") { Mode = BindingMode.OneWay });
            hdr.Bind(HdrBitmapView.ToneMappingProperty, new Binding("ToneMappingMode.Value") { Mode = BindingMode.OneWay });
            hdr.Bind(HdrBitmapView.ToneMappingExposureProperty, new Binding("ToneMappingExposure.Value") { Mode = BindingMode.OneWay });
            newImage = hdr;
        }
        else
        {
            var sdr = new BitmapView();
            sdr.Bind(BitmapView.SourceProperty, new Binding("PreviewImage.Value") { Mode = BindingMode.OneWay });
            sdr.Bind(BitmapView.ToneMappingProperty, new Binding("ToneMappingMode.Value") { Mode = BindingMode.OneWay });
            sdr.Bind(BitmapView.ToneMappingExposureProperty, new Binding("ToneMappingExposure.Value") { Mode = BindingMode.OneWay });
            newImage = sdr;
        }

        image = newImage;

        // Insert after imageBackground, before pathEditorView
        framePanel.Children.Insert(1, newImage);

        _boundsSubscription = newImage.GetObservable(BoundsProperty)
            .Subscribe(bounds =>
            {
                imageBackground.Width = bounds.Width;
                imageBackground.Height = bounds.Height;
                pathEditorView.Width = bounds.Width;
                pathEditorView.Height = bounds.Height;
                transformHandlesOverlay.Width = bounds.Width;
                transformHandlesOverlay.Height = bounds.Height;
                UpdateTransformHandles();
            });
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (e.Root is TopLevel topLevel)
        {
            topLevel.ScalingChanged += OnTopLevelScalingChanged;
        }
        UpdateMaxFrameSize();
    }

    protected override async void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (e.Root is TopLevel topLevel)
        {
            topLevel.ScalingChanged -= OnTopLevelScalingChanged;
        }
        base.OnDetachedFromVisualTree(e);
        _imageConfigSubscription?.Dispose();
        _boundsSubscription?.Dispose();

        InvalidateTransformHandleResource();

        if (DataContext is PlayerViewModel viewModel && viewModel.IsPlaying.Value)
        {
            await viewModel.Pause();
        }
    }

    private void OnTopLevelScalingChanged(object? sender, EventArgs e)
    {
        UpdateMaxFrameSize();
    }

    private void UpdateMaxFrameSize()
    {
        if (DataContext is PlayerViewModel player && TopLevel.GetTopLevel(this) is { } topLevel)
        {
            var size = framePanel.Bounds.Size;
            player.MaxFrameSize = new BtlSize(
                (float)(size.Width * topLevel.RenderScaling),
                (float)(size.Height * topLevel.RenderScaling));
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _disposables.Clear();

        InvalidateTransformHandleResource();

        if (DataContext is PlayerViewModel vm)
        {
            vm.PreviewInvalidated += Player_PreviewInvalidated;
            Disposable.Create(vm, x => x.PreviewInvalidated -= Player_PreviewInvalidated)
                .DisposeWith(_disposables);

            vm.BeginEditTimecodeRequested
                .ObserveOnUIDispatcher()
                .Subscribe(_ => Player.BeginEditCurrentTime())
                .DisposeWith(_disposables);

            if (vm.Scene is { } scene)
            {
                WirePlayerMarkers(scene).DisposeWith(_disposables);
            }
            else
            {
                Player.Markers = Array.Empty<PlayerMarkerEntry>();
            }

            vm.FrameMatrix
                .ObserveOnUIDispatcher()
                .Select(matrix => (matrix, image, framePanel.Children?.FirstOrDefault()!))
                .Where(t => t.Item3 != null)
                .Subscribe(t =>
                {
                    framePanel.RenderTransformOrigin = Avalonia.RelativePoint.TopLeft;
                    framePanel.RenderTransform = new ImmutableTransform(t.matrix.ToAvaMatrix());
                    if (DataContext is PlayerViewModel { Scene: { } } vm)
                    {
                        int width = vm.Scene.FrameSize.Width;
                        if (width == 0) return;
                        double actualWidth = t.image.Bounds.Width * t.matrix.M11;
                        double pixelSize = actualWidth / width;
                        if (pixelSize >= 1)
                        {
                            RenderOptions.SetBitmapInterpolationMode(t.image, BitmapInterpolationMode.None);
                        }
                        else
                        {
                            RenderOptions.SetBitmapInterpolationMode(t.image, BitmapInterpolationMode.HighQuality);
                        }
                    }
                })
                .DisposeWith(_disposables);

            vm.IsHandMode.CombineLatest(vm.IsCropMode, vm.IsCameraMode)
                .ObserveOnUIDispatcher()
                .Subscribe(t =>
                {
                    if (t.First)
                        framePanel.Cursor = Cursors.Hand;
                    else if (t.Second)
                        framePanel.Cursor = Cursors.Cross;
                    else if (t.Third)
                        framePanel.Cursor = Cursors.Cross;
                    else
                        framePanel.Cursor = null;
                })
                .DisposeWith(_disposables);

            var selection = vm.EditViewModel.GetRequiredService<Beutl.Editor.Services.IEditorSelection>();
            selection.SelectedObject
                .ObserveOnUIDispatcher()
                .Subscribe(_ => UpdateTransformHandles())
                .DisposeWith(_disposables);

            // RenderThread.Invoke during playback would stall frame generation, so suppress overlay
            // updates outside Move mode + paused state.
            vm.IsMoveMode.CombineLatest(vm.IsPlaying, (move, playing) => move && !playing)
                .DistinctUntilChanged()
                .ObserveOnUIDispatcher()
                .Subscribe(visible =>
                {
                    transformHandlesOverlay.IsVisible = visible;
                    if (visible) UpdateTransformHandles();
                    else ClearTransformHandleOverlay();
                })
                .DisposeWith(_disposables);
        }
    }

    private void UpdateTransformHandles()
    {
        if (DataContext is not PlayerViewModel vm) return;
        if (!vm.IsMoveMode.Value)
        {
            ClearTransformHandleOverlay();
            return;
        }

        var selection = vm.EditViewModel.GetRequiredService<Beutl.Editor.Services.IEditorSelection>();
        var clock = vm.EditViewModel.GetRequiredService<Beutl.Editor.Services.IEditorClock>();
        if (selection.SelectedObject.Value is not Element element)
        {
            ClearTransformHandleOverlay();
            return;
        }

        TimeSpan time = clock.CurrentTime.Value;
        if (time < element.Start || time >= element.Range.End)
        {
            ClearTransformHandleOverlay();
            return;
        }

        var renderer = vm.EditViewModel.Renderer.Value;
        var scene = vm.EditViewModel.Scene;
        if (image.Bounds.Width <= 0 || scene.FrameSize.Width <= 0)
        {
            ClearTransformHandleOverlay();
            return;
        }

        double frameScale = image.Bounds.Width / scene.FrameSize.Width;
        // Prefer the last hit-tested drawable so the overlay tracks the visual object the user actually
        // clicked on, not just the element's first drawable.
        BtlDrawable? hitTested = _lastSelected.TryGetTarget(out BtlDrawable? cached) ? cached : null;
        BtlDrawable? drawable = null;
        foreach (BtlDrawable d in element.Objects.OfType<BtlDrawable>())
        {
            drawable ??= d;
            if (ReferenceEquals(d, hitTested))
            {
                drawable = d;
                break;
            }
        }
        if (drawable == null)
        {
            ClearTransformHandleOverlay();
            return;
        }

        // Use an independent Resource on RenderThread so we evaluate animations against ctxTime
        // without piggybacking on the renderer's cached render node.
        (BtlSize localSize, BtlMatrix userMatrix, BtlPoint pivotLocal)? snap;
        try
        {
            BtlDrawable target = drawable;
            BtlSize availableSize = scene.FrameSize.ToSize(1.0f);
            TimeSpan ctxTime = time;
            snap = RenderThread.Dispatcher.Invoke(() =>
            {
                BtlRect? bounds = renderer.GetBoundary(target);
                if (bounds is not { Width: > 0, Height: > 0 }) return null;

                var ctx = new CompositionContext(ctxTime);
                if (_transformHandleResource == null || _transformHandleResourceTarget != target)
                {
                    _transformHandleResource?.Dispose();
                    _transformHandleResource = target.ToResource(ctx);
                    _transformHandleResourceTarget = target;
                }
                else
                {
                    // updateOnly=true: this is a read-only view for the overlay, so don't bump Version
                    // and invalidate downstream consumers.
                    bool updateOnly = true;
                    _transformHandleResource.Update(target, ctx, ref updateOnly);
                }
                BtlDrawable.Resource? resource = _transformHandleResource;

                BtlSize localSize = target.MeasureInternal(availableSize, resource);
                if (localSize.Width <= 0 || localSize.Height <= 0) return null;

                BtlMatrix userMatrix = target.GetTransformMatrix(availableSize, localSize, resource);
                BtlPoint pivot = resource.TransformOrigin.ToPixels(localSize);

                // userMatrix omits FilterEffect-induced offsets; align against rendered bounds.
                BtlMatrix adjusted = TransformHandleMath.AlignUserMatrixToRenderedBounds(userMatrix, localSize, bounds.Value);

                return ((BtlSize, BtlMatrix, BtlPoint)?)(localSize, adjusted, pivot);
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(
                ex,
                "GetBoundary/MeasureInternal called from invalid thread for {DrawableType} on element '{Element}'.",
                drawable.GetType().Name, element.Name);
            // Partially-updated cache may remain — dispose and let it rebuild.
            ClearTransformHandleOverlay();
            return;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _logger.LogError(
                ex,
                "Unexpected exception while querying transform overlay geometry (drawable={DrawableType}, element='{Element}').",
                drawable.GetType().Name, element.Name);
            ClearTransformHandleOverlay();
            return;
        }

        if (snap is not { } s)
        {
            ClearTransformHandleOverlay();
            return;
        }

        transformHandlesOverlay.Update(drawable, element, s.localSize, s.userMatrix, s.pivotLocal, image.Bounds.Size, frameScale);
    }

    private void ClearTransformHandleOverlay()
    {
        transformHandlesOverlay.Clear();
        InvalidateTransformHandleResource();
    }

    private void InvalidateTransformHandleResource()
    {
        if (_transformHandleResource == null) return;

        // _transformHandleResource is created inside RenderThread.Dispatcher.Invoke, so the matching
        // Dispose must run there too — UI-thread paths (OnDataContextChanged, OnDetachedFromVisualTree,
        // ClearTransformHandleOverlay) would otherwise race in-flight RenderThread updates.
        RenderThread.Dispatcher.Invoke(() =>
        {
            _transformHandleResource?.Dispose();
            _transformHandleResource = null;
            _transformHandleResourceTarget = null;
        });
    }

    private IDisposable WirePlayerMarkers(Scene scene)
    {
        var disposables = new CompositeDisposable();
        var markerSubscriptions = new Dictionary<SceneMarker, IDisposable>();

        void Refresh()
        {
            var snapshot = new PlayerMarkerEntry[scene.Markers.Count];
            for (int i = 0; i < scene.Markers.Count; i++)
            {
                SceneMarker m = scene.Markers[i];
                snapshot[i] = new PlayerMarkerEntry(
                    m.Name ?? string.Empty,
                    // Player の CurrentTime 表示 (hh\:mm\:ss\.ff) と桁を揃える。
                    m.Time.ToString(@"hh\:mm\:ss\.ff", CultureInfo.InvariantCulture));
            }
            Player.Markers = snapshot;
        }

        void Subscribe(SceneMarker marker)
        {
            if (markerSubscriptions.ContainsKey(marker)) return;
            void OnMarkerPropertyChanged(object? s, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(SceneMarker.Name)
                    || e.PropertyName == nameof(SceneMarker.Time))
                {
                    Refresh();
                }
            }
            marker.PropertyChanged += OnMarkerPropertyChanged;
            markerSubscriptions[marker] = Disposable.Create(
                () => marker.PropertyChanged -= OnMarkerPropertyChanged);
        }

        void Unsubscribe(SceneMarker marker)
        {
            if (markerSubscriptions.Remove(marker, out IDisposable? sub))
            {
                sub.Dispose();
            }
        }

        foreach (SceneMarker marker in scene.Markers)
        {
            Subscribe(marker);
        }

        void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (object? item in e.OldItems)
                {
                    if (item is SceneMarker marker) Unsubscribe(marker);
                }
            }
            if (e.NewItems != null)
            {
                foreach (object? item in e.NewItems)
                {
                    if (item is SceneMarker marker) Subscribe(marker);
                }
            }
            Refresh();
        }

        scene.Markers.CollectionChanged += OnCollectionChanged;
        disposables.Add(Disposable.Create(
            () => scene.Markers.CollectionChanged -= OnCollectionChanged));
        disposables.Add(Disposable.Create(() =>
        {
            foreach (IDisposable sub in markerSubscriptions.Values)
            {
                sub.Dispose();
            }
            markerSubscriptions.Clear();
        }));

        Refresh();
        return disposables;
    }

    private void Player_PreviewInvalidated(object? sender, EventArgs e)
    {
        if (image == null)
            return;

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            image.InvalidateVisual();

            // Skip overlay updates while playing — RenderThread Invoke would block next-frame generation.
            // When playback stops, the IsPlaying subscriber triggers it once.
            if (DataContext is PlayerViewModel { IsPlaying.Value: true }) return;

            UpdateTransformHandles();
        });
    }

    private void ToggleDragModeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton button && DataContext is PlayerViewModel { PathEditor: PathEditorViewModel viewModel })
        {
            viewModel.Symmetry.Value = false;
            viewModel.Asymmetry.Value = false;
            viewModel.Separately.Value = false;

            switch (button.Tag)
            {
                case "Symmetry":
                    viewModel.Symmetry.Value = true;
                    break;
                case "Asymmetry":
                    viewModel.Asymmetry.Value = true;
                    break;
                case "Separately":
                    viewModel.Separately.Value = true;
                    break;
            }
        }
    }
}
