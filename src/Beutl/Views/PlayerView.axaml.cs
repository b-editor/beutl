using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Threading;

using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PathEditorTab.ViewModels;
using Beutl.Logging;
using Beutl.ViewModels;

using Microsoft.Extensions.Logging;

using Reactive.Bindings.Extensions;

namespace Beutl.Views;

public partial class PlayerView : UserControl
{
    private readonly CompositeDisposable _disposables = [];
    private readonly ILogger _logger = Log.CreateLogger<PlayerView>();
    private IDisposable? _imageConfigSubscription;
    private IDisposable? _boundsSubscription;
    internal Control image = null!;

    public PlayerView()
    {
        InitializeComponent();

        SetupImageControl();

        ConfigureFrameContextMenu(framePanel);
        framePanel.PointerPressed += OnFramePointerPressed;
        framePanel.PointerReleased += OnFramePointerReleased;
        framePanel.PointerMoved += OnFramePointerMoved;
        framePanel.AddHandler(PointerWheelChangedEvent, OnFramePointerWheelChanged, RoutingStrategies.Tunnel);

        framePanel.Focusable = true;
        framePanel.KeyDown += OnFrameKeyDown;
        framePanel.KeyUp += OnFrameKeyUp;

        framePanel.GetObservable(BoundsProperty)
            .Subscribe(s =>
            {
                if (DataContext is PlayerViewModel player)
                {
                    player.MaxFrameSize = new((float)s.Size.Width, (float)s.Size.Height);
                }
            });

        // PlayerView.axaxml.DragAndDrop.cs
        DragDrop.SetAllowDrop(framePanel, true);
        framePanel.AddHandler(DragDrop.DragOverEvent, OnFrameDragOver);
        framePanel.AddHandler(DragDrop.DropEvent, OnFrameDrop);
    }

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
            });
    }

    protected override async void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _imageConfigSubscription?.Dispose();
        _boundsSubscription?.Dispose();
        if (DataContext is PlayerViewModel viewModel && viewModel.IsPlaying.Value)
        {
            await viewModel.Pause();
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _disposables.Clear();
        if (DataContext is PlayerViewModel vm)
        {
            vm.PreviewInvalidated += Player_PreviewInvalidated;
            Disposable.Create(vm, x => x.PreviewInvalidated -= Player_PreviewInvalidated)
                .DisposeWith(_disposables);

            vm.FrameMatrix
                .ObserveOnUIDispatcher()
                .Select(matrix => (matrix, image, framePanel.Children?.FirstOrDefault()!))
                .Where(t => t.Item3 != null)
                .Subscribe(t =>
                {
                    framePanel.RenderTransformOrigin = RelativePoint.TopLeft;
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
        }
    }

    private void Player_PreviewInvalidated(object? sender, EventArgs e)
    {
        if (image == null)
            return;

        Dispatcher.UIThread.InvokeAsync(image.InvalidateVisual);
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
