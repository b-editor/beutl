using System.Numerics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
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
    private double _touchScale = 1;

    private void OnContentScrollTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        if (e.NameScope.Find<ScrollContentPresenter>("PART_ContentPresenter") is not { } presenter) return;
        var existing = presenter.GestureRecognizers.OfType<ScrollGestureRecognizer>().FirstOrDefault();
        if (existing is TimelineTouchGestureRecognizer) return;
        if (existing != null) presenter.GestureRecognizers.Remove(existing);

        var gesture = new TimelineTouchGestureRecognizer();
        gesture.Bind(ScrollGestureRecognizer.CanHorizontallyScrollProperty, presenter.GetObservable(ScrollContentPresenter.CanHorizontallyScrollProperty));
        gesture.Bind(ScrollGestureRecognizer.CanVerticallyScrollProperty, presenter.GetObservable(ScrollContentPresenter.CanVerticallyScrollProperty));
        gesture.Bind(ScrollGestureRecognizer.OffsetProperty, presenter.GetObservable(ScrollContentPresenter.OffsetProperty).Select(offset => (Avalonia.Vector?)offset));
        gesture.Bind(ScrollGestureRecognizer.ViewportProperty, presenter.GetObservable(ScrollContentPresenter.ViewportProperty).Select(viewport => (Size?)viewport));
        gesture.Bind(ScrollGestureRecognizer.ExtentProperty, presenter.GetObservable(ScrollContentPresenter.ExtentProperty).Select(extent => (Size?)extent));
        gesture.Bind(ScrollGestureRecognizer.IsScrollInertiaEnabledProperty, presenter.GetObservable(ScrollViewer.IsScrollInertiaEnabledProperty));
        presenter.GestureRecognizers.Add(gesture);
    }

    private void OnTimelineTouchPinch(object? sender, PinchEventArgs e)
    {
        if (ViewModel is { } viewModel && e.Source is Visual source && double.IsFinite(e.Scale) && e.Scale > 0
            && source.TranslatePoint(e.ScaleOrigin, TimelinePanel) is { } origin)
        {
            _mouseFlag = TimelineHelper.MouseFlags.Free;
            UpdateZoom(viewModel, e.Scale / _touchScale, origin.X);
            _touchScale = e.Scale;
        }
        e.Handled = true;
    }

    private void OnTimelineTouchPinchEnded(object? sender, PinchEndedEventArgs e) => _touchScale = 1;

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
}
