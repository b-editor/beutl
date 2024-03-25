using System.Numerics;
using System.Text.Json.Nodes;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;

using Beutl.Configuration;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.Models;
using Beutl.Operators.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;
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
        RangeSelectionPressed
    }

    internal MouseFlags _mouseFlag = MouseFlags.Free;
    internal TimeSpan _pointerFrame;
    private bool _rightButtonPressed;
    private readonly ILogger _logger = Log.CreateLogger<Timeline>();
    private TimelineViewModel? _viewModel;
    private readonly CompositeDisposable _disposables = [];
    private ElementView? _selectedElement;
    private readonly List<(ElementViewModel Element, bool IsSelectedOriginal)> _rangeSelection = [];
    private CancellationTokenSource? _scrollCts;

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
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (DataContext is TimelineViewModel viewModel)
        {
            // KeyBindingsは変更してはならない。
            foreach (KeyBinding binding in viewModel.KeyBindings)
            {
                if (e.Handled)
                    break;
                binding.TryHandle(e);
            }
        }
    }

    private void OnDataContextDetached(TimelineViewModel obj)
    {
        _viewModel = null;

        TimelinePanel.Children.RemoveRange(2, TimelinePanel.Children.Count - 2);
        _selectedElement = null;
        _rangeSelection.Clear();

        _disposables.Clear();
    }

    private void OnDataContextAttached(TimelineViewModel vm)
    {
        _viewModel = vm;

        TimelinePanel.Children.AddRange(vm.Elements.SelectMany(e =>
        {
            return new Control[]
            {
                new ElementView
                {
                    DataContext = e
                },
                new ElementScopeView
                {
                    DataContext = e.Scope
                }
            };
        }));

        TimelinePanel.Children.AddRange(vm.Inlines.Select(e => new InlineAnimationLayer
        {
            DataContext = e
        }));

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

        ViewModel.Paste.Subscribe(PasteCore)
            .DisposeWith(_disposables);

        ViewModel.ScrollTo.Subscribe(v => ScrollTimelinePosition(v.Range, v.ZIndex))
            .DisposeWith(_disposables);

        ViewModel.EditorContext.SelectedObject.Subscribe(e =>
            {
                if (_selectedElement != null)
                {
                    foreach (ElementViewModel item in ViewModel.Elements.GetMarshal().Value)
                    {
                        item.IsSelected.Value = false;
                    }

                    _selectedElement = null;
                }

                if (e is Element element && FindElementView(element) is ElementView { DataContext: ElementViewModel viewModel } newView)
                {
                    viewModel.IsSelected.Value = true;
                    _selectedElement = newView;
                }
            })
            .DisposeWith(_disposables);
    }

    private async void PasteCore()
    {
        try
        {
            if (TopLevel.GetTopLevel(this) is { Clipboard: IClipboard clipboard })
            {
                string[] formats = await clipboard.GetFormatsAsync();

                if (formats.Contains(Constants.Element))
                {
                    string? json = await clipboard.GetTextAsync();
                    if (json != null)
                    {
                        var oldElement = new Element();

                        var context = new JsonSerializationContext(
                            oldElement.GetType(), NullSerializationErrorNotifier.Instance, json: JsonNode.Parse(json)!.AsObject());
                        using (ThreadLocalSerializationContext.Enter(context))
                        {
                            oldElement.Deserialize(context);
                        }

                        CoreObjectReborn.Reborn(oldElement, out Element newElement);

                        newElement.Start = ViewModel.ClickedFrame;
                        newElement.ZIndex = ViewModel.CalculateClickedLayer();

                        newElement.Save(RandomFileNameGenerator.Generate(Path.GetDirectoryName(ViewModel.Scene.FileName)!, Constants.ElementFileExtension));

                        CommandRecorder recorder = ViewModel.EditorContext.CommandRecorder;
                        ViewModel.Scene.AddChild(newElement).DoAndRecord(recorder);

                        ScrollTimelinePosition(newElement.Range, newElement.ZIndex);
                    }
                }
                else
                {
                    string[] imageFormats = ["image/png", "PNG", "image/jpeg", "image/jpg"];

                    if (Array.Find(imageFormats, i => formats.Contains(i)) is { } matchFormat)
                    {
                        object? imageData = await clipboard.GetDataAsync(matchFormat);
                        Stream? stream = null;
                        if (imageData is byte[] byteArray)
                            stream = new MemoryStream(byteArray);
                        else if (imageData is Stream st)
                            stream = st;

                        if (stream?.CanRead != true)
                        {
                            NotificationService.ShowWarning(
                                "タイムライン",
                                $"この画像データはペーストできません\nFormats: [{string.Join(", ", formats)}]");
                        }
                        else
                        {
                            string dir = Path.GetDirectoryName(ViewModel.Scene.FileName)!;
                            // 画像を保存
                            string resDir = Path.Combine(dir, "resources");
                            if (!Directory.Exists(resDir))
                            {
                                Directory.CreateDirectory(resDir);
                            }
                            string imageFile = RandomFileNameGenerator.Generate(resDir, "png");
                            using (var bmp = Bitmap<Bgra8888>.FromStream(stream))
                            {
                                bmp.Save(imageFile, Graphics.EncodedImageFormat.Png);
                            }

                            var sp = new SourceImageOperator
                            {
                                Source = { Value = BitmapSource.Open(imageFile) }
                            };
                            var newElement = new Element
                            {
                                Start = ViewModel.ClickedFrame,
                                Length = TimeSpan.FromSeconds(5),
                                ZIndex = ViewModel.CalculateClickedLayer(),
                                Operation = { Children = { sp } },
                                AccentColor = ColorGenerator.GenerateColor(typeof(SourceImageOperator).FullName!),
                                Name = Path.GetFileName(imageFile)
                            };

                            newElement.Save(RandomFileNameGenerator.Generate(dir, Constants.ElementFileExtension));

                            CommandRecorder recorder = ViewModel.EditorContext.CommandRecorder;
                            ViewModel.Scene.AddChild(newElement).DoAndRecord(recorder);

                            ScrollTimelinePosition(newElement.Range, newElement.ZIndex);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception has occurred.");
            NotificationService.ShowError(Message.AnUnexpectedErrorHasOccurred, ex.Message);
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

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

    internal TimelineViewModel ViewModel => _viewModel!;

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
        TimelineViewModel viewModel = ViewModel;
        Avalonia.Vector aOffset = ContentScroll.Offset;
        var offset = new Vector2((float)aOffset.X, (float)aOffset.Y);

        viewModel.Options.Value = viewModel.Options.Value with
        {
            Offset = offset
        };
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
        TimelineViewModel viewModel = ViewModel;
        Avalonia.Vector aOffset = ContentScroll.Offset;
        Avalonia.Vector delta = e.Delta;
        float scale = viewModel.Options.Value.Scale;
        var offset = new Vector2((float)aOffset.X, (float)aOffset.Y);

        if (e.KeyModifiers == KeyModifiers.Control)
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

        viewModel.Options.Value = viewModel.Options.Value with
        {
            Scale = scale,
            Offset = offset
        };

        e.Handled = true;
    }

    // ポインター移動
    private void TimelinePanel_PointerMoved(object? sender, PointerEventArgs e)
    {
        TimelineViewModel viewModel = ViewModel;
        PointerPoint pointerPt = e.GetCurrentPoint(TimelinePanel);
        int rate = viewModel.Scene.FindHierarchicalParent<Project>().GetFrameRate();
        _pointerFrame = pointerPt.Position.X.ToTimeSpan(viewModel.Options.Value.Scale).RoundToRate(rate);

        if (_pointerFrame >= viewModel.Scene.Duration)
        {
            _pointerFrame = viewModel.Scene.Duration - TimeSpan.FromSeconds(1d / rate);
        }
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
        else
        {
            Point posScale = e.GetPosition(Scale);

            if (Scale.IsPointerOver && posScale.Y > Scale.Bounds.Height - 8)
            {
                BufferStatusViewModel.CacheBlock[] cacheBlocks = viewModel.EditorContext.BufferStatus.CacheBlocks.Value;

                viewModel.HoveredCacheBlock.Value = Array.Find(cacheBlocks, v => new TimeRange(v.Start, v.Length).Contains(_pointerFrame));
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
        PointerPoint pointerPt = e.GetCurrentPoint(TimelinePanel);

        if (pointerPt.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased)
        {
            if (_mouseFlag == MouseFlags.RangeSelectionPressed)
            {
                overlay.SelectionRange = default;
                _rangeSelection.Clear();
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
        TimelineViewModel viewModel = ViewModel;
        foreach ((ElementViewModel element, bool isSelectedOriginal) in _rangeSelection)
        {
            element.IsSelected.Value = isSelectedOriginal;
        }

        _rangeSelection.Clear();
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
                _rangeSelection.Add((item, item.IsSelected.Value));
                item.IsSelected.Value = true;
            }
        }
    }

    // ポインターが押された
    private void TimelinePanel_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        TimelineViewModel viewModel = ViewModel;
        PointerPoint pointerPt = e.GetCurrentPoint(TimelinePanel);
        viewModel.ClickedFrame = pointerPt.Position.X.ToTimeSpan(viewModel.Options.Value.Scale)
            .RoundToRate(viewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30);

        viewModel.ClickedPosition = pointerPt.Position;

        TimelinePanel.Focus();

        if (pointerPt.Properties.IsLeftButtonPressed)
        {
            if (e.KeyModifiers == KeyModifiers.Control)
            {
                _mouseFlag = MouseFlags.RangeSelectionPressed;
                overlay.SelectionRange = new(pointerPt.Position, default(Size));
            }
            else
            {
                _mouseFlag = MouseFlags.SeekBarPressed;
                viewModel.EditorContext.CurrentTime.Value = viewModel.ClickedFrame;
            }
        }

        _rightButtonPressed = pointerPt.Properties.IsRightButtonPressed;
    }

    // ポインターが離れた
    private void TimelinePanel_PointerExited(object? sender, PointerEventArgs e)
    {
        _mouseFlag = MouseFlags.Free;

        if (!_rightButtonPressed)
        {
            ViewModel.HoveredCacheBlock.Value = null;
        }
    }

    // ドロップされた
    private async void TimelinePanel_Drop(object? sender, DragEventArgs e)
    {
        TimelinePanel.Cursor = Cursors.Arrow;
        TimelineViewModel viewModel = ViewModel;
        Scene scene = ViewModel.Scene;
        Point pt = e.GetPosition(TimelinePanel);

        viewModel.ClickedFrame = pt.X.ToTimeSpan(viewModel.Options.Value.Scale)
            .RoundToRate(viewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30);
        viewModel.ClickedPosition = pt;

        if (e.Data.Get(KnownLibraryItemFormats.SourceOperator) is Type type)
        {
            if (e.KeyModifiers == KeyModifiers.Control)
            {
                CommandRecorder recorder = ViewModel.EditorContext.CommandRecorder;
                var dialog = new AddElementDialog
                {
                    DataContext = new AddElementDialogViewModel(
                        scene,
                        new ElementDescription(
                            viewModel.ClickedFrame,
                            TimeSpan.FromSeconds(5),
                            viewModel.CalculateClickedLayer(),
                            InitialOperator: type),
                        recorder)
                };
                await dialog.ShowAsync();
            }
            else
            {
                viewModel.AddElement.Execute(new ElementDescription(
                    viewModel.ClickedFrame, TimeSpan.FromSeconds(5), viewModel.CalculateClickedLayer(), InitialOperator: type));
            }
        }
        else if (e.Data.GetFiles()
            ?.Where(v => v is IStorageFile)
            ?.Select(v => v.TryGetLocalPath())
            .FirstOrDefault(v => v != null) is { } fileName)
        {
            viewModel.AddElement.Execute(new ElementDescription(
                viewModel.ClickedFrame, TimeSpan.FromSeconds(5), viewModel.CalculateClickedLayer(), FileName: fileName));
        }
    }

    private void TimelinePanel_DragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(KnownLibraryItemFormats.SourceOperator)
            || (e.Data.GetFiles()?.Any() ?? false))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    // 要素を追加
    private async void AddElementClick(object? sender, RoutedEventArgs e)
    {
        CommandRecorder recorder = ViewModel.EditorContext.CommandRecorder;
        var dialog = new AddElementDialog
        {
            DataContext = new AddElementDialogViewModel(ViewModel.Scene,
                new ElementDescription(ViewModel.ClickedFrame, TimeSpan.FromSeconds(5), ViewModel.CalculateClickedLayer()),
                recorder)
        };
        await dialog.ShowAsync();
    }

    private void ShowSceneSettings(object? sender, RoutedEventArgs e)
    {
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
        var view = new ElementView
        {
            DataContext = viewModel
        };
        var scopeView = new ElementScopeView
        {
            DataContext = viewModel.Scope
        };

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
        var view = new InlineAnimationLayer
        {
            DataContext = viewModel
        };

        TimelinePanel.Children.Add(view);
    }

    private void OnRemovedInline(InlineAnimationLayerViewModel viewModel)
    {
        IAbstractAnimatableProperty prop = viewModel.Property;
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
        return TimelinePanel.Children.FirstOrDefault(ctr => ctr.DataContext is ElementViewModel vm && vm.Model == element) as ElementView;
    }

    private void ZoomClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is MenuFlyoutItem menuItem)
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

            ViewModel.Options.Value = ViewModel.Options.Value with
            {
                Scale = zoom,
            };
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
                        Setters =
                        {
                            new Setter(ScrollViewer.OffsetProperty, ContentScroll.Offset),
                        }
                    },
                    new KeyFrame()
                    {
                        Cue = new Cue(1),
                        Setters =
                        {
                            new Setter(ScrollViewer.OffsetProperty, new Avalonia.Vector(newOffsetX, newOffsetY)),
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

    private void Binding(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
    }
}
