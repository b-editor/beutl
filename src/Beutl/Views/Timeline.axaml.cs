using System.Numerics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Beutl.Configuration;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Tools;
using Beutl.Views.Dialogs;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;

namespace Beutl.Views;

public sealed partial class Timeline : UserControl
{
    internal enum MouseFlags
    {
        Free,
        SeekBarPressed,
        RangeSelectionPressed,
        EndingBarMarkerPressed,
        StartingBarMarkerPressed
    }

    internal MouseFlags _mouseFlag = MouseFlags.Free;
    internal TimeSpan _pointerFrame;
    private TimeSpan _initialStart;
    private TimeSpan _initialDuration;
    private bool _rightButtonPressed;
    private readonly ILogger _logger = Log.CreateLogger<Timeline>();
    private readonly CompositeDisposable _disposables = [];
    private ElementView? _selectedElement;
    private CancellationTokenSource? _scrollCts;

    // 長方形マーカーのサイズを定義
    private const int MarkerHeight = 18;
    private const int MarkerWidth = 4;

    public Timeline()
    {
        InitializeComponent();

        gridSplitter.DragDelta += GridSplitter_DragDelta;

        Scale.AddHandler(PointerWheelChangedEvent, ContentScroll_PointerWheelChanged, RoutingStrategies.Tunnel);
        ContentScroll.AddHandler(PointerWheelChangedEvent, ContentScroll_PointerWheelChanged, RoutingStrategies.Tunnel);

        TimelinePanel.AddHandler(DragDrop.DragOverEvent, TimelinePanel_DragOver);
        TimelinePanel.AddHandler(DragDrop.DropEvent, TimelinePanel_Drop);
        DragDrop.SetAllowDrop(TimelinePanel, true);

        this.SubscribeDataContextChange<TimelineViewModel>(OnDataContextAttached, OnDataContextDetached);

        PopulateAddElementSubMenu();
    }

    private void OnDataContextDetached(TimelineViewModel obj)
    {
        ViewModel = null;

        TimelinePanel.Children.RemoveRange(2, TimelinePanel.Children.Count - 2);
        _selectedElement = null;

        _disposables.Clear();
    }

    private void OnDataContextAttached(TimelineViewModel vm)
    {
        ViewModel = vm;

        TimelinePanel.Children.AddRange(vm.Elements.SelectMany(e =>
        {
            return new Control[]
            {
                new ElementView { DataContext = e }, new ElementScopeView { DataContext = e.Scope }
            };
        }));

        TimelinePanel.Children.AddRange(vm.Inlines.Select(e => new InlineAnimationLayer { DataContext = e }));

        vm.Elements.TrackCollectionChanged(
                AddElement,
                RemoveElement,
                () => { })
            .DisposeWith(_disposables);

        vm.Inlines.TrackCollectionChanged(
                OnAddedInline,
                OnRemovedInline,
                () => { })
            .DisposeWith(_disposables);

        ViewModel.ScrollTo.Subscribe(v => ScrollTimelinePosition(v.Range, v.ZIndex))
            .DisposeWith(_disposables);

        ViewModel.EditorContext.SelectedObject.Subscribe(e =>
            {
                if (_selectedElement != null)
                {
                    ViewModel.ClearSelected();

                    _selectedElement = null;
                }

                if (e is Element element && FindElementView(element) is
                    { DataContext: ElementViewModel viewModel } newView)
                {
                    _selectedElement = newView;
                    ViewModel.SelectElement(viewModel);
                }
            })
            .DisposeWith(_disposables);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (ViewModel == null)
        {
            _logger.LogWarning("Timeline loaded without ViewModel.");
            return;
        }

        ViewModel.EditorContext.Options.Subscribe(options =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Vector2 offset = options.Offset;
                    ContentScroll.Offset = new(offset.X, offset.Y);
                    PaneScroll.Offset = new(0, offset.Y);
                }, DispatcherPriority.MaxValue);
            })
            .DisposeWith(_disposables);

        ContentScroll.ScrollChanged += ContentScroll_ScrollChanged;
    }

    internal TimelineViewModel? ViewModel { get; private set; }

    private void GridSplitter_DragDelta(object? sender, VectorEventArgs e)
    {
        ColumnDefinition def = grid.ColumnDefinitions[0];
        double last = def.ActualWidth + e.Vector.X;

        if (last is < 395 and > 385)
        {
            def.MaxWidth = 390;
            def.MinWidth = 390;
        }
        else
        {
            def.MaxWidth = double.PositiveInfinity;
            def.MinWidth = 200;
        }
    }

    // PaneScrollがスクロールされた
    private void PaneScroll_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        ContentScroll.Offset = ContentScroll.Offset.WithY(PaneScroll.Offset.Y);
    }

    // PaneScrollがスクロールされた
    private void ContentScroll_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (ViewModel == null) return;
        TimelineViewModel viewModel = ViewModel;
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
        TimelineViewModel viewModel = ViewModel;
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
            if (OperatingSystem.IsWindows() && e.KeyModifiers == KeyModifiers.Shift)
            {
                delta = delta.SwapAxis();
            }

            if (GlobalConfiguration.Instance.EditorConfig.SwapTimelineScrollDirection)
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

    // ポインター移動
    private void TimelinePanel_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (ViewModel == null) return;
        TimelineViewModel viewModel = ViewModel;
        PointerPoint pointerPt = e.GetCurrentPoint(TimelinePanel);
        int rate = viewModel.Scene.FindHierarchicalParent<Project>().GetFrameRate();
        _pointerFrame = pointerPt.Position.X.ToTimeSpan(viewModel.Options.Value.Scale).RoundToRate(rate);

        if (_pointerFrame < TimeSpan.Zero)
        {
            _pointerFrame = TimeSpan.Zero;
        }

        if (_mouseFlag == MouseFlags.SeekBarPressed)
        {
            viewModel.EditorContext.CurrentTime.Value = _pointerFrame;
        }
        else if (_mouseFlag == MouseFlags.RangeSelectionPressed)
        {
            Rect rect = overlay.SelectionRange;
            overlay.SelectionRange = new(rect.Position, pointerPt.Position);
            UpdateRangeSelection();
        }
        else if (_mouseFlag == MouseFlags.EndingBarMarkerPressed)
        {
            // ポインタ位置に基づいてシーンDurationを更新
            TimeSpan newDuration = _pointerFrame - viewModel.Scene.Start;
            if (newDuration < TimeSpan.Zero)
            {
                newDuration = TimeSpan.FromSeconds(1d / rate);
            }

            // 直接値を更新（コマンド記録なし）
            viewModel.Scene.Duration = newDuration;
        }
        else if (_mouseFlag == MouseFlags.StartingBarMarkerPressed)
        {
            // Calculate the new starting point for the scene based on the pointer frame
            TimeSpan clampedStart = _pointerFrame;

            // Ensure the new start time is not negative
            if (clampedStart < TimeSpan.Zero)
            {
                clampedStart = TimeSpan.Zero;
            }
            // Ensure the new start time does not exceed the total duration
            else if (clampedStart > _initialDuration + _initialStart)
            {
                clampedStart = _initialDuration + _initialStart - TimeSpan.FromSeconds(1d / rate);
            }

            // Update the scene's start and duration based on the clamped start time
            viewModel.Scene.Start = clampedStart;
            viewModel.Scene.Duration = _initialDuration + _initialStart - clampedStart;
        }
        else
        {
            Point posScale = e.GetPosition(Scale);
            double startingBarX = viewModel.StartingBarMargin.Value.Left;
            double endingBarX = viewModel.EndingBarMargin.Value.Left;

            // EndingBarマーカーの当たり判定チェック
            if (IsPointInTimelineScaleMarker(pointerPt.Position.X, posScale.Y, startingBarX, endingBarX))
            {
                Scale.Cursor = Cursors.SizeWestEast;
            }
            else
            {
                Scale.Cursor = Cursors.Arrow;
            }

            if (Scale.IsPointerOver && posScale.Y > Scale.Bounds.Height - 8)
            {
                BufferStatusViewModel.CacheBlock[] cacheBlocks = viewModel.EditorContext.BufferStatus.CacheBlocks.Value;

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

        if (pointerPt.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased)
        {
            if (_mouseFlag == MouseFlags.RangeSelectionPressed)
            {
                overlay.SelectionRange = default;
            }
            else if (_mouseFlag == MouseFlags.EndingBarMarkerPressed)
            {
                ViewModel.EditorContext.HistoryManager.Commit(CommandNames.ChangeSceneDuration);
            }
            else if (_mouseFlag == MouseFlags.StartingBarMarkerPressed)
            {
                ViewModel.EditorContext.HistoryManager.Commit(CommandNames.ChangeSceneStart);
            }

            if (Scale.IsPointerOver && ViewModel.HoveredCacheBlock.Value is { } cache)
            {
                FrameCacheManager cacheManager = ViewModel.EditorContext.FrameCacheManager.Value;
                long size = cacheManager.CalculateByteCount(cache.StartFrame, cache.StartFrame + cache.LengthFrame);

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
        TimelineViewModel viewModel = ViewModel;
        viewModel.ClearSelected();

        Rect rect = overlay.SelectionRange.Normalize();
        var startTime = rect.Left.ToTimeSpan(viewModel.Options.Value.Scale);
        var endTime = rect.Right.ToTimeSpan(viewModel.Options.Value.Scale);
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

    // TimelineScaleの長方形マーカーの当たり判定
    // GraphEditorViewでも使用
    internal static bool IsPointInTimelineScaleMarker(double x, double y, double startingBarX, double endingBarX)
    {
        return IsPointInTimelineScaleStartingMarker(x, y, startingBarX) ||
               IsPointInTimelineScaleEndingMarker(x, y, endingBarX);
    }

    internal static bool IsPointInTimelineScaleStartingMarker(double x, double y, double startingBarX)
    {
        // 長方形の範囲を計算
        var startRect = new Rect(startingBarX, 0, MarkerWidth, MarkerHeight);

        // 点が長方形内にあるか判定
        return startRect.Contains(new Point(x, y));
    }

    internal static bool IsPointInTimelineScaleEndingMarker(double x, double y, double endingBarX)
    {
        // 長方形の範囲を計算
        var endRect = new Rect(endingBarX - MarkerWidth, 0, MarkerWidth, MarkerHeight);

        // 点が長方形内にあるか判定
        return endRect.Contains(new Point(x, y));
    }

    // ポインターが押された
    private void TimelinePanel_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel == null) return;
        TimelineViewModel viewModel = ViewModel;
        PointerPoint pointerPt = e.GetCurrentPoint(TimelinePanel);
        viewModel.ClickedFrame = pointerPt.Position.X.ToTimeSpan(viewModel.Options.Value.Scale)
            .RoundToRate(viewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30);

        viewModel.ClickedPosition = pointerPt.Position;

        TimelinePanel.Focus();

        if (pointerPt.Properties.IsLeftButtonPressed)
        {
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

                // マーカーの当たり判定チェック - TimelineScaleのマーカーのみ
                if (IsPointInTimelineScaleEndingMarker(pointerPt.Position.X, scalePoint.Y, endingBarX))
                {
                    _mouseFlag = MouseFlags.EndingBarMarkerPressed;
                    // 初期値を保存
                    _initialDuration = viewModel.Scene.Duration;
                }
                else if (IsPointInTimelineScaleStartingMarker(pointerPt.Position.X, scalePoint.Y, startingBarX))
                {
                    _mouseFlag = MouseFlags.StartingBarMarkerPressed;
                    _initialStart = viewModel.Scene.Start; // 初期値を保存
                    _initialDuration = viewModel.Scene.Duration;
                }
                else
                {
                    _mouseFlag = MouseFlags.SeekBarPressed;
                    viewModel.EditorContext.CurrentTime.Value = viewModel.ClickedFrame;
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

    // ドロップされた
    private async void TimelinePanel_Drop(object? sender, DragEventArgs e)
    {
        if (ViewModel == null) return;
        TimelinePanel.Cursor = Cursors.Arrow;
        TimelineViewModel viewModel = ViewModel;
        Scene scene = ViewModel.Scene;
        Point pt = e.GetPosition(TimelinePanel);

        viewModel.ClickedFrame = pt.X.ToTimeSpan(viewModel.Options.Value.Scale)
            .RoundToRate(viewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30);
        viewModel.ClickedPosition = pt;

        if (e.DataTransfer.TryGetValue(BeutlDataFormats.SourceOperator) is { } typeName
            && TypeFormat.ToType(typeName) is { } type)
        {
            if (e.KeyModifiers == KeyModifiers.Control)
            {
                var dialog = new AddElementDialog
                {
                    DataContext = new AddElementDialogViewModel(
                        scene,
                        new ElementDescription(
                            viewModel.ClickedFrame,
                            TimeSpan.FromSeconds(5),
                            viewModel.CalculateClickedLayer(),
                            InitialOperator: type),
                        ViewModel.EditorContext.HistoryManager)
                };
                await dialog.ShowAsync();
            }
            else
            {
                viewModel.AddElement.Execute(new ElementDescription(
                    viewModel.ClickedFrame, TimeSpan.FromSeconds(5), viewModel.CalculateClickedLayer(),
                    InitialOperator: type));
            }
        }
        else if (e.DataTransfer.TryGetFile()?.TryGetLocalPath() is { } fileName)
        {
            viewModel.AddElement.Execute(new ElementDescription(
                viewModel.ClickedFrame, TimeSpan.FromSeconds(5), viewModel.CalculateClickedLayer(),
                FileName: fileName));
        }
    }

    private void TimelinePanel_DragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(BeutlDataFormats.SourceOperator)
            || e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
    }

    // 要素を追加
    private async void AddElementClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var dialog = new AddElementDialog
        {
            DataContext = new AddElementDialogViewModel(ViewModel.Scene,
                new ElementDescription(ViewModel.ClickedFrame, TimeSpan.FromSeconds(5),
                    ViewModel.CalculateClickedLayer()),
                ViewModel.EditorContext.HistoryManager)
        };
        await dialog.ShowAsync();
    }



    private void PopulateAddElementSubMenu()
    {
        foreach (LibraryItem item in LibraryService.Current.Items)
        {
            Control? menuItem = CreateMenuItemForLibraryItem(item);
            if (menuItem != null)
            {
                AddElementSubMenu.Items.Add(menuItem);
            }
        }
    }

    private Control? CreateMenuItemForLibraryItem(LibraryItem item)
    {
        switch (item)
        {
            case SingleTypeLibraryItem single when single.Format == KnownLibraryItemFormats.SourceOperator:
                {
                    var menuItem = new MenuFlyoutItem { Text = single.DisplayName, Tag = single.ImplementationType };
                    menuItem.Click += AddElementWithTypeClick;
                    return menuItem;
                }

            case MultipleTypeLibraryItem multiple when multiple.Types.TryGetValue(KnownLibraryItemFormats.SourceOperator, out Type? type):
                {
                    var menuItem = new MenuFlyoutItem { Text = multiple.DisplayName, Tag = type };
                    menuItem.Click += AddElementWithTypeClick;
                    return menuItem;
                }

            case GroupLibraryItem group:
                {
                    var subItems = new List<Control>();
                    foreach (LibraryItem child in group.Items)
                    {
                        Control? childItem = CreateMenuItemForLibraryItem(child);
                        if (childItem != null)
                        {
                            subItems.Add(childItem);
                        }
                    }

                    if (subItems.Count == 0)
                        return null;

                    var subMenu = new MenuFlyoutSubItem { Text = group.DisplayName };
                    foreach (Control subItem in subItems)
                    {
                        subMenu.Items.Add(subItem);
                    }
                    return subMenu;
                }

            default:
                return null;
        }
    }

    private void AddElementWithTypeClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        if (sender is not MenuFlyoutItem { Tag: Type operatorType }) return;

        ViewModel.AddElement.Execute(new ElementDescription(
            ViewModel.ClickedFrame,
            TimeSpan.FromSeconds(5),
            ViewModel.CalculateClickedLayer(),
            InitialOperator: operatorType));
    }

    private void ShowSceneSettings(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        EditViewModel editorContext = ViewModel.EditorContext;
        SceneSettingsTabViewModel? tab = editorContext.FindToolTab<SceneSettingsTabViewModel>();
        if (tab != null)
        {
            tab.IsSelected.Value = true;
        }
        else
        {
            editorContext.OpenToolTab(new SceneSettingsTabViewModel(editorContext));
        }
    }

    // 要素を追加
    private void AddElement(int index, ElementViewModel viewModel)
    {
        var view = new ElementView { DataContext = viewModel };
        var scopeView = new ElementScopeView { DataContext = viewModel.Scope };

        TimelinePanel.Children.Add(view);
        TimelinePanel.Children.Add(scopeView);
    }

    // 要素を削除
    private void RemoveElement(int index, ElementViewModel viewModel)
    {
        Element elm = viewModel.Model;

        for (int i = TimelinePanel.Children.Count - 1; i >= 0; i--)
        {
            Control item = TimelinePanel.Children[i];
            if ((item.DataContext is ElementViewModel vm1 && vm1.Model == elm)
                || (item.DataContext is ElementScopeViewModel vm2 && vm2.Model == elm))
            {
                TimelinePanel.Children.RemoveAt(i);
            }
        }
    }

    private void OnAddedInline(InlineAnimationLayerViewModel viewModel)
    {
        var view = new InlineAnimationLayer { DataContext = viewModel };

        TimelinePanel.Children.Add(view);
    }

    private void OnRemovedInline(InlineAnimationLayerViewModel viewModel)
    {
        IAnimatablePropertyAdapter prop = viewModel.Property;
        for (int i = 0; i < TimelinePanel.Children.Count; i++)
        {
            Control item = TimelinePanel.Children[i];
            if (item.DataContext is InlineAnimationLayerViewModel vm && vm.Property == prop)
            {
                TimelinePanel.Children.RemoveAt(i);
                break;
            }
        }
    }

    private ElementView? FindElementView(Element element)
    {
        return TimelinePanel.Children.FirstOrDefault(ctr =>
            ctr.DataContext is ElementViewModel vm && vm.Model == element) as ElementView;
    }

    private void ZoomClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is MenuFlyoutItem menuItem && ViewModel != null)
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
            double pointerPos = _pointerFrame.ToPixel(ViewModel.Options.Value.Scale);
            double deltaLeft = pointerPos - offset.X;
            offset.X = (float)((pointerPos / oldScale * zoom) - deltaLeft);
            ViewModel.Options.Value = ViewModel.Options.Value with { Scale = zoom, Offset = offset };
        }
    }

    private async void ScrollTimelinePosition(TimeRange range, int zindex)
    {
        if (DataContext is TimelineViewModel viewModel)
        {
            const double Spacing = 40;

            float scale = viewModel.Options.Value.Scale;
            Size viewport = ContentScroll.Viewport - new Size(Spacing * 2, 0);
            Avalonia.Vector offset = ContentScroll.Offset + new Avalonia.Vector(Spacing, 0);

            var start = offset.X.ToTimeSpan(scale);
            var length = viewport.Width.ToTimeSpan(scale);
            int startZIndex = viewModel.ToLayerNumber(offset.Y);
            int endZIndex = viewModel.ToLayerNumber(offset.Y + viewport.Height);

            double newOffsetX = ContentScroll.Offset.X;
            double newOffsetY = ContentScroll.Offset.Y;

            if (!range.Intersects(new TimeRange(start, length)))
            {
                newOffsetX = range.Start.ToPixel(scale) - Spacing;
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
