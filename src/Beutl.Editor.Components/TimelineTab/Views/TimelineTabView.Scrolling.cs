using System.Numerics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Media;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.TimelineTab.Views;

public sealed partial class TimelineTabView
{
    // Left and right of a fitted range, as the graph editor leaves around its keyframes.
    private const double FitMargin = 32;

    // How far a zoom has come, from the view it started at (0) to the fitted one (1). The settle animation drives it.
    private static readonly StyledProperty<double> ZoomProgressProperty =
        AvaloniaProperty.Register<TimelineTabView, double>("ZoomProgress");

    private TimelineHelper.ZoomMotion? _zoomMotion;

    private void PaneScroll_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        ContentScroll.Offset = ContentScroll.Offset.WithY(PaneScroll.Offset.Y);
    }

    private void ContentScroll_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        InvalidateElementViewport();
        // The first layout reports the offset the viewer starts at. OnLoaded has yet to apply the saved one.
        if (ViewModel == null || !IsLoaded) return;
        TimelineTabViewModel viewModel = ViewModel;
        Avalonia.Vector aOffset = ContentScroll.Offset;
        var offset = new Vector2((float)aOffset.X, (float)aOffset.Y);

        viewModel.Options.Value = viewModel.Options.Value with { Offset = offset };
    }

    private void UpdateZoom(TimelineTabViewModel viewModel, double factor, double pointerX)
    {
        Avalonia.Vector aOffset = ContentScroll.Offset;
        float oldScale = viewModel.Options.Value.Scale;
        var offset = new Vector2((float)aOffset.X, (float)aOffset.Y);

        float scale = (float)Math.Clamp(
            factor * oldScale,
            Beutl.ProjectSystem.TimelineOptions.MinScale,
            Beutl.ProjectSystem.TimelineOptions.MaxScale);

        offset.X = TimelineHelper.ZoomOffsetX(pointerX, offset.X, oldScale, scale);
        viewModel.Options.Value = viewModel.Options.Value with { Scale = scale, Offset = offset };
    }

    private void ContentScroll_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (ViewModel == null) return;
        TimelineTabViewModel viewModel = ViewModel;
        StopZooming();

        if (e.KeyModifiers == KeyGestureHelper.GetCommandModifier())
        {
            UpdateZoom(viewModel, MathF.Pow(1.2f, (float)e.Delta.Y), e.GetCurrentPoint(TimelinePanel).Position.X);
        }
        else
        {
            Avalonia.Vector aOffset = ContentScroll.Offset;
            var offset = new Vector2((float)aOffset.X, (float)aOffset.Y);
            Avalonia.Vector delta = TimelineHelper.GetScrollOffsetDelta(e, _usesGestureAxes(e), viewModel.EditorContext.GetRequiredService<GlobalConfiguration>().EditorConfig);
            offset.X += (float)delta.X;
            offset.Y += (float)delta.Y;
            viewModel.Options.Value = viewModel.Options.Value with { Offset = offset };
        }

        e.Handled = true;
    }

    private void OnTimelineMagnify(object? sender, PointerDeltaEventArgs e)
    {
        if (ViewModel is { } viewModel && NativeScrollInput.TryGetMagnifyFactor(e, out double factor))
        {
            StopZooming();
            UpdateZoom(viewModel, factor, e.GetCurrentPoint(TimelinePanel).Position.X);
        }

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
            offset.X = TimelineHelper.ZoomOffsetX(pointerPos, offset.X, oldScale, zoom);
            ViewModel.Options.Value = ViewModel.Options.Value with { Scale = zoom, Offset = offset };
        }
    }

    private void OnCurrentTimeChangedForAutoScroll(MediaTime currentTime)
    {
        if (ViewModel == null) return;

        TimelineHelper.AutoScrollToPlayhead(ContentScroll, ViewModel.EditorContext, ViewModel.Options, currentTime);
    }

    private async Task ScrollTimelinePosition(TimeRange range, int zindex)
    {
        if (DataContext is TimelineTabViewModel viewModel)
        {
            const double Spacing = 40;

            float scale = viewModel.Options.Value.Scale;
            Size viewport = ContentScroll.Viewport - new Size(Spacing * 2, 0);
            Avalonia.Vector offset = ContentScroll.Offset + new Avalonia.Vector(Spacing, 0);

            var start = offset.X.PixelToTime(scale);
            var length = viewport.Width.PixelToTime(scale);

            double newOffsetX = ContentScroll.Offset.X;
            double newOffsetY = OffsetYShowing(viewModel, zindex);

            if (!range.Intersects(new TimeRange(start, length)))
            {
                newOffsetX = range.Start.TimeToPixel(scale) - Spacing;
            }

            _scrollCts?.Cancel();
            using var cancellation = new CancellationTokenSource();
            _scrollCts = cancellation;
            var anm = TimelineSettleAnimation.Create(
                TimeSpan.FromSeconds(0.5),
                FillMode.None,
                [new Setter(ScrollViewer.OffsetProperty, ContentScroll.Offset)],
                [new Setter(ScrollViewer.OffsetProperty, new Avalonia.Vector(newOffsetX, newOffsetY))]);
            try
            {
                await anm.RunAsync(ContentScroll, cancellation.Token);
                if (cancellation.IsCancellationRequested || ViewModel != viewModel) return;
                ContentScroll.ClearValue(ScrollViewer.OffsetProperty);
                ContentScroll.Offset = new Avalonia.Vector(newOffsetX, newOffsetY);
                viewModel.Options.Value = viewModel.Options.Value with
                {
                    Offset = new Vector2((float)newOffsetX, (float)newOffsetY)
                };
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            finally { if (ReferenceEquals(_scrollCts, cancellation)) _scrollCts = null; }
        }
    }

    // The vertical offset that shows the layer: the current one while the layer is in view, or else the layer's top.
    private double OffsetYShowing(TimelineTabViewModel viewModel, int zindex)
    {
        double top = ContentScroll.Offset.Y;
        int first = viewModel.ToLayerNumber(top);
        int last = viewModel.ToLayerNumber(top + ContentScroll.Viewport.Height);
        return first <= zindex && zindex <= last ? top : viewModel.CalculateLayerTop(zindex);
    }

    // Zooms and scrolls together until the range fills the width, on the curve and for the time a scroll takes.
    private async Task ZoomTimelineTo(TimeRange range, int? zindex)
    {
        if (DataContext is not TimelineTabViewModel viewModel || ContentScroll.Viewport.Width <= 0) return;

        (float scale, double offsetX) = TimelineHelper.FitRange(range, ContentScroll.Viewport.Width, FitMargin);
        var target = new Avalonia.Vector(offsetX, zindex is { } layer ? OffsetYShowing(viewModel, layer) : ContentScroll.Offset.Y);

        _scrollCts?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _scrollCts = cancellation;
        var motion = new TimelineHelper.ZoomMotion(viewModel.Options.Value.Scale, ContentScroll.Offset, scale, target);
        _zoomMotion = motion;
        var anm = TimelineSettleAnimation.Create(
            TimeSpan.FromSeconds(0.5),
            FillMode.None,
            [new Setter(ZoomProgressProperty, 0d)],
            [new Setter(ZoomProgressProperty, 1d)]);
        try
        {
            await anm.RunAsync(this, cancellation.Token);
            if (cancellation.IsCancellationRequested || ViewModel != viewModel) return;
            ShowView(viewModel, scale, target);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally
        {
            if (ReferenceEquals(_scrollCts, cancellation)) _scrollCts = null;
            if (ReferenceEquals(_zoomMotion, motion)) _zoomMotion = null;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // Only the animation's frames move the view, not the value it falls back to when it ends or stops.
        if (change.Property == ZoomProgressProperty && change.Priority == BindingPriority.Animation
            && _zoomMotion is { } motion && ViewModel is { } viewModel)
        {
            (float scale, Avalonia.Vector offset) = motion.At(change.GetNewValue<double>());
            ShowView(viewModel, scale, offset);
        }
    }

    // An animation frame comes before its layout, and the options reach the scroll viewers only after that layout,
    // which would lay the frame out at the new scale and the old offset. So the offsets are set here as well.
    private void ShowView(TimelineTabViewModel viewModel, float scale, Avalonia.Vector offset)
    {
        viewModel.Options.Value = viewModel.Options.Value with
        {
            Scale = scale,
            Offset = new Vector2((float)offset.X, (float)offset.Y)
        };
        ContentScroll.Offset = offset;
        PaneScroll.Offset = new Avalonia.Vector(0, offset.Y);
    }

    // The wheel and a pinch take over from a zoom in progress.
    private void StopZooming()
    {
        if (_zoomMotion is null) return;

        _zoomMotion = null;
        _scrollCts?.Cancel();
    }
}
