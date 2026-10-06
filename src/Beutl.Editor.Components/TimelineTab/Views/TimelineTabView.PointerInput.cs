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
    // ポインター移動
    private void TimelinePanel_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (ViewModel == null) return;
        TimelineTabViewModel viewModel = ViewModel;
        PointerPoint pointerPt = e.GetCurrentPoint(TimelinePanel);
        int rate = viewModel.Scene.FindHierarchicalParent<Project>().GetFrameRate();
        _pointerFrame = pointerPt.Position.X.PixelToTimeSpan(viewModel.Options.Value.Scale).RoundToRate(rate);

        if (_pointerFrame < TimeSpan.Zero)
        {
            _pointerFrame = TimeSpan.Zero;
        }

        if (_mouseFlag == MouseFlags.SeekBarPressed)
        {
            viewModel.CurrentTime.Value = _pointerFrame;
        }
        else if (_mouseFlag == MouseFlags.RangeSelectionPressed)
        {
            Rect rect = overlay.SelectionRange;
            overlay.SelectionRange = new(rect.Position, pointerPt.Position);
            UpdateRangeSelection();
        }
        else if (_mouseFlag == MouseFlags.EndingBarMarkerPressed)
        {
            viewModel.EditorContext
                .GetRequiredService<ISceneTimeRangeService>()
                .UpdateEndDrag(viewModel.Scene, _pointerFrame);
        }
        else if (_mouseFlag == MouseFlags.MarkerPressed && _pressedMarker is { } draggingMarker)
        {
            // 閾値を超えるまではドラッグとみなさず、クリックでのフライアウト表示の余地を残す
            if (!_markerDragged
                && Math.Abs(pointerPt.Position.X - _markerPressPosition.X) < MarkerDragThreshold
                && Math.Abs(pointerPt.Position.Y - _markerPressPosition.Y) < MarkerDragThreshold)
            {
                return;
            }

            _markerDragged = true;
            draggingMarker.Time = _pointerFrame;
        }
        else if (_mouseFlag == MouseFlags.StartingBarMarkerPressed)
        {
            viewModel.EditorContext
                .GetRequiredService<ISceneTimeRangeService>()
                .UpdateStartDrag(viewModel.Scene, _pointerFrame, _initialStart, _initialDuration);
        }
        else
        {
            Point posScale = e.GetPosition(Scale);
            double startingBarX = viewModel.StartingBarMargin.Value.Left;
            double endingBarX = viewModel.EndingBarMargin.Value.Left;

            // EndingBarマーカーやポイントマーカーの当たり判定チェック
            if (TimelineHelper.IsPointInTimelineScaleMarker(pointerPt.Position.X, posScale.Y, startingBarX, endingBarX)
                || (Scale.IsPointerOver && Scale.HitTestMarker(pointerPt.Position.X, posScale.Y) != null))
            {
                Scale.Cursor = Cursors.SizeWestEast;
            }
            else
            {
                Scale.Cursor = Cursors.Arrow;
            }

            if (Scale.IsPointerOver && posScale.Y > Scale.Bounds.Height - 8)
            {
                CacheBlock[] cacheBlocks = viewModel.BufferStatus.CacheBlocks.Value;

                viewModel.HoveredCacheBlock.Value = Array.Find(cacheBlocks,
                    v => new TimeRange(v.Start, v.Length).Contains(_pointerFrame));
            }
            else
            {
                viewModel.HoveredCacheBlock.Value = null;
            }
        }
    }

    // ポインターが放された
    private void TimelinePanel_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (ViewModel == null) return;
        PointerPoint pointerPt = e.GetCurrentPoint(TimelinePanel);
        HistoryManager history = ViewModel.EditorContext.GetRequiredService<HistoryManager>();

        if (pointerPt.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased)
        {
            if (_mouseFlag == MouseFlags.RangeSelectionPressed)
            {
                overlay.SelectionRange = default;
            }
            else if (_mouseFlag == MouseFlags.EndingBarMarkerPressed)
            {
                ViewModel.EditorContext
                    .GetRequiredService<ISceneTimeRangeService>()
                    .CommitEndChange();
            }
            else if (_mouseFlag == MouseFlags.StartingBarMarkerPressed)
            {
                ViewModel.EditorContext
                    .GetRequiredService<ISceneTimeRangeService>()
                    .CommitStartChange();
            }
            else if (_mouseFlag == MouseFlags.MarkerPressed && _pressedMarker is { } releasedMarker)
            {
                if (_markerDragged)
                {
                    if (releasedMarker.Time != _markerInitialTime)
                    {
                        history.Commit(CommandNames.MoveMarker);
                    }
                }
                else
                {
                    // 移動が閾値未満ならクリック扱いで編集フライアウトを開く
                    ShowMarkerEditFlyout(releasedMarker);
                }

                _pressedMarker = null;
                _markerDragged = false;
            }

            if (Scale.IsPointerOver && ViewModel.HoveredCacheBlock.Value is { } cache)
            {
                long size = ViewModel.BufferStatus.CalculateCacheByteCount(cache.StartFrame, cache.StartFrame + cache.LengthFrame);

                CacheTip.Content = $"""
                                    {Strings.MemoryUsage}: {Utilities.StringFormats.ToHumanReadableSize(size)}
                                    {Strings.StartTime}: {cache.Start}
                                    {Strings.DurationTime}: {cache.Length}
                                    {(cache.IsLocked ? Strings.Locked : Strings.Unlocked)}
                                    """;
                CacheTip.IsOpen = true;
            }

            _mouseFlag = MouseFlags.Free;
        }
        else if (pointerPt.Properties.PointerUpdateKind == PointerUpdateKind.RightButtonReleased)
        {
            _rightButtonPressed = false;
        }
    }

    private void UpdateRangeSelection()
    {
        if (ViewModel == null) return;
        TimelineTabViewModel viewModel = ViewModel;
        viewModel.ClearSelected();

        Rect rect = overlay.SelectionRange.Normalize();
        var startTime = rect.Left.PixelToTimeSpan(viewModel.Options.Value.Scale);
        var endTime = rect.Right.PixelToTimeSpan(viewModel.Options.Value.Scale);
        var timeRange = TimeRange.FromRange(startTime, endTime);

        int startLayer = viewModel.ToLayerNumber(rect.Top);
        int endLayer = viewModel.ToLayerNumber(rect.Bottom);

        foreach (ElementViewModel item in viewModel.Elements)
        {
            if (timeRange.Intersects(item.Model.Range)
                && startLayer <= item.Model.ZIndex && item.Model.ZIndex <= endLayer)
            {
                viewModel.SelectElement(item);
            }
        }
    }

    // ポインターが押された
    private void TimelinePanel_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel == null) return;
        TimelineTabViewModel viewModel = ViewModel;
        PointerPoint pointerPt = e.GetCurrentPoint(TimelinePanel);
        viewModel.ClickedFrame = pointerPt.Position.X.PixelToTimeSpan(viewModel.Options.Value.Scale)
            .RoundToRate(viewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30);

        viewModel.ClickedPosition = pointerPt.Position;

        TimelinePanel.Focus();

        if (pointerPt.Properties.IsLeftButtonPressed)
        {
            if (viewModel.IsRazorMode.Value)
            {
                viewModel.RazorSplitAt(viewModel.ClickedFrame, acrossAllLayers: true);
                e.Handled = true;
                _rightButtonPressed = false;
                return;
            }

            if (e.KeyModifiers == KeyGestureHelper.GetCommandModifier())
            {
                _mouseFlag = MouseFlags.RangeSelectionPressed;
                // すでに選択されているものはリセット
                ViewModel.ClearSelected();

                overlay.SelectionRange = new(pointerPt.Position, default(Size));
            }
            else
            {
                double endingBarX = viewModel.EndingBarMargin.Value.Left;
                double startingBarX = viewModel.StartingBarMargin.Value.Left;
                Point scalePoint = e.GetPosition(Scale);

                // ポイントマーカーの当たり判定（Scale 上端の三角ピン）
                if (Scale.IsPointerOver
                    && Scale.HitTestMarker(pointerPt.Position.X, scalePoint.Y) is { } hitMarker)
                {
                    _mouseFlag = MouseFlags.MarkerPressed;
                    _pressedMarker = hitMarker;
                    _markerInitialTime = hitMarker.Time;
                    _markerPressPosition = pointerPt.Position;
                    _markerDragged = false;
                    e.Handled = true;
                    return;
                }

                // マーカーの当たり判定チェック - TimelineScaleのマーカーのみ
                if (TimelineHelper.IsPointInTimelineScaleEndingMarker(pointerPt.Position.X, scalePoint.Y, endingBarX))
                {
                    _mouseFlag = MouseFlags.EndingBarMarkerPressed;
                    _initialStart = viewModel.Scene.Start;
                    _initialDuration = viewModel.Scene.Duration;
                }
                else if (TimelineHelper.IsPointInTimelineScaleStartingMarker(pointerPt.Position.X, scalePoint.Y, startingBarX))
                {
                    _mouseFlag = MouseFlags.StartingBarMarkerPressed;
                    _initialStart = viewModel.Scene.Start;
                    _initialDuration = viewModel.Scene.Duration;
                }
                else
                {
                    _mouseFlag = MouseFlags.SeekBarPressed;
                    viewModel.CurrentTime.Value = viewModel.ClickedFrame;
                }
            }
        }

        _rightButtonPressed = pointerPt.Properties.IsRightButtonPressed;
    }

    // ポインターが離れた
    private void TimelinePanel_PointerExited(object? sender, PointerEventArgs e)
    {
        if (ViewModel == null) return;

        if (!_rightButtonPressed)
        {
            ViewModel.HoveredCacheBlock.Value = null;
        }
    }
}
