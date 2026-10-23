using System.Numerics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Media;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.TimelineTab.Views;

public sealed partial class TimelineTabView
{
    private void PaneScroll_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        ContentScroll.Offset = ContentScroll.Offset.WithY(PaneScroll.Offset.Y);
    }

    private void ContentScroll_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (ViewModel == null) return;
        TimelineTabViewModel viewModel = ViewModel;
        Avalonia.Vector aOffset = ContentScroll.Offset;
        var offset = new Vector2((float)aOffset.X, (float)aOffset.Y);

        viewModel.Options.Value = viewModel.Options.Value with { Offset = offset };
    }

    private void UpdateZoom(PointerWheelEventArgs e, ref float scale, ref Vector2 offset)
    {
        float oldScale = scale;
        Point pointerPos = e.GetCurrentPoint(TimelinePanel).Position;

        const float ZoomSpeed = 1.2f;
        float delta = (float)e.Delta.Y;

        scale = MathF.Pow(ZoomSpeed, delta) * scale;
        scale = Math.Min(scale, 2);

        offset.X = ZoomOffsetX(pointerPos.X, offset.X, oldScale, scale);
    }

    // Keeps the content under pointerX in place while the scale changes.
    private static float ZoomOffsetX(double pointerX, float offsetX, float oldScale, float newScale)
    {
        double deltaLeft = pointerX - offsetX;
        return (float)((pointerX / oldScale * newScale) - deltaLeft);
    }

    private void ContentScroll_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (ViewModel == null) return;
        TimelineTabViewModel viewModel = ViewModel;
        Avalonia.Vector aOffset = ContentScroll.Offset;
        Avalonia.Vector delta = e.Delta;
        float scale = viewModel.Options.Value.Scale;
        var offset = new Vector2((float)aOffset.X, (float)aOffset.Y);

        if (e.KeyModifiers == KeyGestureHelper.GetCommandModifier())
        {
            UpdateZoom(e, ref scale, ref offset);
        }
        else
        {
            bool gestureAxes = _usesGestureAxes(e);
            if (!gestureAxes && OperatingSystem.IsWindows() && e.KeyModifiers == KeyModifiers.Shift)
            {
                delta = new Avalonia.Vector(delta.Y, delta.X);
            }

            // Touchpad gestures already follow the gesture axes and OS direction.
            if (gestureAxes || GlobalConfiguration.Instance.EditorConfig.SwapTimelineScrollDirection.Value)
            {
                offset.Y -= (float)(delta.Y * 50);
                offset.X -= (float)(delta.X * 50);
            }
            else
            {
                offset.X -= (float)(delta.Y * 50);
                offset.Y -= (float)(delta.X * 50);
            }
        }

        viewModel.Options.Value = viewModel.Options.Value with { Scale = scale, Offset = offset };

        e.Handled = true;
    }

    private void ZoomClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is FAMenuFlyoutItem menuItem && ViewModel != null
            && TimelineHelper.TryParseZoom(menuItem.CommandParameter, out float zoom))
        {
            float oldScale = ViewModel.Options.Value.Scale;
            var offset = ViewModel.Options.Value.Offset;
            double pointerPos = _pointerFrame.TimeToPixel(ViewModel.Options.Value.Scale);
            offset.X = ZoomOffsetX(pointerPos, offset.X, oldScale, zoom);
            ViewModel.Options.Value = ViewModel.Options.Value with { Scale = zoom, Offset = offset };
        }
    }

    private void OnCurrentTimeChangedForAutoScroll(MediaTime currentTime)
    {
        if (ViewModel == null) return;

        TimelineHelper.AutoScrollToPlayhead(ContentScroll, ViewModel.EditorContext, ViewModel.Options, currentTime);
    }

    private async void ScrollTimelinePosition(TimeRange range, int zindex)
    {
        if (DataContext is TimelineTabViewModel viewModel)
        {
            const double Spacing = 40;

            float scale = viewModel.Options.Value.Scale;
            Size viewport = ContentScroll.Viewport - new Size(Spacing * 2, 0);
            Avalonia.Vector offset = ContentScroll.Offset + new Avalonia.Vector(Spacing, 0);

            var start = offset.X.PixelToTime(scale);
            var length = viewport.Width.PixelToTime(scale);
            int startZIndex = viewModel.ToLayerNumber(offset.Y);
            int endZIndex = viewModel.ToLayerNumber(offset.Y + viewport.Height);

            double newOffsetX = ContentScroll.Offset.X;
            double newOffsetY = ContentScroll.Offset.Y;

            if (!range.Intersects(new TimeRange(start, length)))
            {
                newOffsetX = range.Start.TimeToPixel(scale) - Spacing;
            }

            if (!(startZIndex <= zindex && zindex <= endZIndex))
            {
                newOffsetY = viewModel.CalculateLayerTop(zindex);
            }

            _scrollCts?.Cancel();
            _scrollCts = new CancellationTokenSource();
            var anm = TimelineSettleAnimation.Create(
                TimeSpan.FromSeconds(0.5),
                FillMode.None,
                [new Setter(ScrollViewer.OffsetProperty, ContentScroll.Offset)],
                [new Setter(ScrollViewer.OffsetProperty, new Avalonia.Vector(newOffsetX, newOffsetY))]);
            await anm.RunAsync(ContentScroll, _scrollCts.Token);
            ContentScroll.ClearValue(ScrollViewer.OffsetProperty);
            ContentScroll.Offset = new Avalonia.Vector(newOffsetX, newOffsetY);
            if (!_scrollCts.IsCancellationRequested)
            {
                viewModel.Options.Value = viewModel.Options.Value with
                {
                    Offset = new Vector2((float)newOffsetX, (float)newOffsetY)
                };
            }
        }
    }
}
