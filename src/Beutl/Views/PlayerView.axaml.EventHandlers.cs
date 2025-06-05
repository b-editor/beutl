using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Beutl.ViewModels;
using Reactive.Bindings.Extensions;
using Size = Beutl.Graphics.Size;

namespace Beutl.Views;

public partial class PlayerView
{
    private void SetupEventHandlers()
    {
        ConfigureFrameContextMenu(framePanel);
        framePanel.PointerPressed += OnFramePointerPressed;
        framePanel.PointerReleased += OnFramePointerReleased;
        framePanel.PointerMoved += OnFramePointerMoved;
        framePanel.AddHandler(PointerWheelChangedEvent, OnFramePointerWheelChanged, RoutingStrategies.Tunnel);

        framePanel.GetObservable(BoundsProperty)
            .Subscribe(s =>
            {
                if (DataContext is PlayerViewModel player)
                {
                    player.MaxFrameSize = new Size((float)s.Size.Width, (float)s.Size.Height);
                }
            });

        // PlayerView.axaxml.DragAndDrop.cs
        DragDrop.SetAllowDrop(framePanel, true);
        framePanel.AddHandler(DragDrop.DragOverEvent, OnFrameDragOver);
        framePanel.AddHandler(DragDrop.DropEvent, OnFrameDrop);
    }

    private void SetupDataContextBindings(PlayerViewModel vm)
    {
        vm.PreviewInvalidated += Player_PreviewInvalidated;
        Disposable.Create(vm, x => x.PreviewInvalidated -= Player_PreviewInvalidated)
            .DisposeWith(_disposables);

        vm.FrameMatrix
            .ObserveOnUIDispatcher()
            .Select(matrix => (matrix, image, framePanel.Children.FirstOrDefault()))
            .Where(t => t.Item3 != null)
            .Subscribe(t =>
            {
                framePanel.RenderTransformOrigin = RelativePoint.TopLeft;
                framePanel.RenderTransform = new ImmutableTransform(t.matrix.ToAvaMatrix());
                if (vm.Scene == null) return;

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
            })
            .DisposeWith(_disposables);

        vm.IsHandMode.CombineLatest(vm.IsCropMode)
            .ObserveOnUIDispatcher()
            .Subscribe(t =>
            {
                if (t.First)
                    framePanel.Cursor = Cursors.Hand;
                else if (t.Second)
                    framePanel.Cursor = Cursors.Cross;
                else
                    framePanel.Cursor = null;
            })
            .DisposeWith(_disposables);
    }

    private void Player_PreviewInvalidated(object? sender, EventArgs e)
    {
        if (image == null) return;

        Dispatcher.UIThread.InvokeAsync(image.InvalidateVisual);
    }

    private void ToggleDragModeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton button) return;
        if (DataContext is not PlayerViewModel { PathEditor: { } viewModel }) return;

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
