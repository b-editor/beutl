using System.Numerics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Editor.Components.FileBrowserTab;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.SceneSettingsTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.Views;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Editor.VersionControl;
using Beutl.Engine;
using Beutl.Logging;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings.Extensions;
using AvaColor = Avalonia.Media.Color;
using BtlColor = Beutl.Media.Color;
using MouseFlags = Beutl.Editor.Components.Helpers.TimelineHelper.MouseFlags;

namespace Beutl.Editor.Components.TimelineTab.Views;

public sealed partial class TimelineTabView
{
    // PaneScrollがスクロールされた
    private void PaneScroll_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        ContentScroll.Offset = ContentScroll.Offset.WithY(PaneScroll.Offset.Y);
    }

    // PaneScrollがスクロールされた
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
        double deltaLeft = pointerPos.X - offset.X;

        const float ZoomSpeed = 1.2f;
        float delta = (float)e.Delta.Y;
        float realDelta = MathF.Sign(delta) * MathF.Abs(delta);

        scale = MathF.Pow(ZoomSpeed, realDelta) * scale;
        scale = Math.Min(scale, 2);

        offset.X = (float)((pointerPos.X / oldScale * scale) - deltaLeft);
    }

    // マウスホイールが動いた
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
            // 目盛りのスケールを変更
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
            if (gestureAxes || GlobalConfiguration.Instance.EditorConfig.SwapTimelineScrollDirection)
            {
                offset.Y -= (float)(delta.Y * 50);
                offset.X -= (float)(delta.X * 50);
            }
            else
            {
                // オフセット(X) をスクロール
                offset.X -= (float)(delta.Y * 50);
                offset.Y -= (float)(delta.X * 50);
            }
        }

        viewModel.Options.Value = viewModel.Options.Value with { Scale = scale, Offset = offset };

        e.Handled = true;
    }

    private void ZoomClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is FAMenuFlyoutItem menuItem && ViewModel != null)
        {
            float zoom;
            switch (menuItem.CommandParameter)
            {
                case string str:
                    if (!float.TryParse(str, out zoom))
                    {
                        return;
                    }

                    break;
                case double zoom1:
                    zoom = (float)zoom1;
                    break;
                case float zoom2:
                    zoom = zoom2;
                    break;
                default:
                    return;
            }

            float oldScale = ViewModel.Options.Value.Scale;
            var offset = ViewModel.Options.Value.Offset;
            double pointerPos = _pointerFrame.TimeToPixel(ViewModel.Options.Value.Scale);
            double deltaLeft = pointerPos - offset.X;
            offset.X = (float)((pointerPos / oldScale * zoom) - deltaLeft);
            ViewModel.Options.Value = ViewModel.Options.Value with { Scale = zoom, Offset = offset };
        }
    }

    private void OnCurrentTimeChangedForAutoScroll(TimeSpan currentTime)
    {
        if (ViewModel == null) return;

        var mode = GlobalConfiguration.Instance.EditorConfig.TimelineAutoScrollMode;
        if (mode == TimelineAutoScrollMode.None) return;

        var previewPlayer = ViewModel.EditorContext.GetService<IPreviewPlayer>();
        if (previewPlayer == null || !previewPlayer.IsPlaying.Value) return;

        float scale = ViewModel.Options.Value.Scale;
        double seekBarPixel = currentTime.TimeToPixel(scale);

        double? newOffsetX = TimelineHelper.CalculateAutoScrollOffset(
            seekBarPixel, ContentScroll.Viewport.Width, ContentScroll.Offset.X, mode);

        if (newOffsetX is not { } offsetX) return;

        ContentScroll.Offset = new Avalonia.Vector(offsetX, ContentScroll.Offset.Y);
    }

    private async void ScrollTimelinePosition(TimeRange range, int zindex)
    {
        if (DataContext is TimelineTabViewModel viewModel)
        {
            const double Spacing = 40;

            float scale = viewModel.Options.Value.Scale;
            Size viewport = ContentScroll.Viewport - new Size(Spacing * 2, 0);
            Avalonia.Vector offset = ContentScroll.Offset + new Avalonia.Vector(Spacing, 0);

            var start = offset.X.PixelToTimeSpan(scale);
            var length = viewport.Width.PixelToTimeSpan(scale);
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
            var anm = new Avalonia.Animation.Animation
            {
                Easing = new SplineEasing(0.1, 0.9, 0.2, 1.0),
                Duration = TimeSpan.FromSeconds(0.5),
                FillMode = FillMode.None,
                Children =
                {
                    new KeyFrame()
                    {
                        Cue = new Cue(0),
                        Setters = { new Setter(ScrollViewer.OffsetProperty, ContentScroll.Offset), }
                    },
                    new KeyFrame()
                    {
                        Cue = new Cue(1),
                        Setters =
                        {
                            new Setter(ScrollViewer.OffsetProperty,
                                new Avalonia.Vector(newOffsetX, newOffsetY)),
                        }
                    }
                }
            };
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
