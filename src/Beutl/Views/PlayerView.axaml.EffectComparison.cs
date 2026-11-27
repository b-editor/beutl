using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Language;
using Beutl.ViewModels;
using Reactive.Bindings.Extensions;

namespace Beutl.Views;

public partial class PlayerView
{
    private IPointer? _effectComparisonDrag;
    private double _effectComparisonDragOffset;

    private void SubscribeEffectComparison(PlayerViewModel? player)
    {
        if (player is null)
        {
            SetEffectComparisonOverlayVisible(false);
            return;
        }

        player.EffectComparisonImage.Select(frame => frame is not null).DistinctUntilChanged()
            .ObserveOnUIDispatcher()
            .Subscribe(visible =>
            {
                if (visible && player.ComparisonTarget is { Effect: var effect })
                {
                    beforeEffectComparisonText.Text = string.Format(Strings.EffectComparisonBefore,
                        TypeDisplayHelpers.GetLocalizedName(effect.GetType()));
                    AutomationProperties.SetName(beforeEffectComparisonLabel, beforeEffectComparisonText.Text);
                    SetEffectComparisonOverlayVisible(true);
                }
                else
                {
                    SetEffectComparisonOverlayVisible(false);
                }
            })
            .DisposeWith(_disposables);
    }

    private void SetEffectComparisonOverlayVisible(bool visible)
    {
        effectComparisonOverlay.IsVisible = visible;
        if (visible) ArrangeEffectComparisonOverlay();
        else EndEffectComparisonDrag();
    }

    private void OnEffectComparisonToggleClick(object? sender, RoutedEventArgs e)
        => (DataContext as PlayerViewModel)?.ToggleEffectComparison();

    private void OnEffectComparisonLabelClick(object? sender, RoutedEventArgs e)
    {
        // The comparison labels vanish while the prompt is visible. Keep keyboard focus in the preview.
        effectComparisonToggle.Focus();
        (DataContext as PlayerViewModel)?.BeginEffectComparisonSelection();
    }

    private void ArrangeEffectComparisonOverlay()
    {
        if (DataContext is not PlayerViewModel player || GetFrameRectIn(previewViewport) is not { } frame)
            return;

        Rect visible = frame.Intersect(new Rect(previewViewport.Bounds.Size));
        double splitX = frame.X + (frame.Width * player.EffectComparisonDivider);
        effectComparisonDivider.IsVisible = visible.Height > 0 && splitX >= visible.Left && splitX <= visible.Right;
        effectComparisonDivider.Height = visible.Height;
        Canvas.SetLeft(effectComparisonDivider, splitX - (effectComparisonDivider.Width / 2));
        Canvas.SetTop(effectComparisonDivider, visible.Top);

        const double inset = 8;
        effectComparisonLabels.IsVisible = visible.Width > 2 * inset && visible.Height > 2 * inset;
        effectComparisonLabels.Width = Math.Max(0, visible.Width - (2 * inset));
        Canvas.SetLeft(effectComparisonLabels, visible.Left + inset);
        Canvas.SetTop(effectComparisonLabels, visible.Top + inset);
    }

    private void OnEffectComparisonDividerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not PlayerViewModel player || GetFrameRectIn(previewViewport) is not { } frame
            || !e.GetCurrentPoint(effectComparisonDivider).Properties.IsLeftButtonPressed)
            return;

        _effectComparisonDrag = e.Pointer;
        _effectComparisonDragOffset = e.GetPosition(previewViewport).X - frame.X - (frame.Width * player.EffectComparisonDivider);
        e.Pointer.Capture(effectComparisonDivider);
        e.Handled = true;
    }

    private void OnEffectComparisonDividerMoved(object? sender, PointerEventArgs e)
    {
        if (!ReferenceEquals(_effectComparisonDrag, e.Pointer) || e.Pointer.Captured != effectComparisonDivider
            || DataContext is not PlayerViewModel player || GetFrameRectIn(previewViewport) is not { Width: > 0 } frame)
            return;

        player.SetEffectComparisonDivider((e.GetPosition(previewViewport).X - _effectComparisonDragOffset - frame.X) / frame.Width);
        ArrangeEffectComparisonOverlay();
        e.Handled = true;
    }

    private void OnEffectComparisonDividerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!ReferenceEquals(_effectComparisonDrag, e.Pointer)) return;
        EndEffectComparisonDrag();
        e.Handled = true;
    }

    private void EndEffectComparisonDrag()
    {
        if (_effectComparisonDrag is { Captured: var captured } pointer && captured == effectComparisonDivider)
            pointer.Capture(null);
        _effectComparisonDrag = null;
    }
}
