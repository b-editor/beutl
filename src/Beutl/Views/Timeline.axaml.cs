using System.Numerics;
using System.Text.Json.Nodes;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;

using Beutl.Framework;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Operation;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Dialogs;
using Beutl.Media;

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
    internal int _pointerLayer;
    private TimelineViewModel? _viewModel;
    private IDisposable? _disposable0;
    private IDisposable? _disposable1;
    private IDisposable? _disposable2;
    private IDisposable? _disposable3;
    private IDisposable? _disposable4;
    private TimelineLayer? _selectedLayer;
    private List<(TimelineLayerViewModel Layer, bool IsSelectedOriginal)> _rangeSelection = new();

    public Timeline()
    {
        InitializeComponent();

        gridSplitter.DragDelta += GridSplitter_DragDelta;

        Scale.AddHandler(PointerWheelChangedEvent, ContentScroll_PointerWheelChanged, RoutingStrategies.Tunnel);
        ContentScroll.AddHandler(PointerWheelChangedEvent, ContentScroll_PointerWheelChanged, RoutingStrategies.Tunnel);

        TimelinePanel.AddHandler(DragDrop.DragOverEvent, TimelinePanel_DragOver);
        TimelinePanel.AddHandler(DragDrop.DropEvent, TimelinePanel_Drop);
        DragDrop.SetAllowDrop(TimelinePanel, true);
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

    // DataContextが変更された
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is TimelineViewModel vm && vm != _viewModel)
        {
            if (_viewModel != null)
            {
                TimelinePanel.Children.RemoveRange(2, TimelinePanel.Children.Count - 2);

                _disposable0?.Dispose();
                _disposable1?.Dispose();
                _disposable2?.Dispose();
                _disposable3?.Dispose();
                _disposable4?.Dispose();
            }

            _viewModel = vm;

            var minHeightBinding = new Binding("Options.Value")
            {
                Source = ViewModel,
                Converter = new FuncValueConverter<TimelineOptions, double>(x => x.MaxLayerCount * Helper.LayerHeight)
            };
            TimelinePanel[!MinHeightProperty] = minHeightBinding;
            LeftPanel[!MinHeightProperty] = minHeightBinding;

            _disposable0 = vm.Layers.ForEachItem(
                AddLayer,
                RemoveLayer,
                () => { });

            _disposable4 = vm.Inlines.ForEachItem(
                OnAddedInline,
                OnRemovedInline,
                () => { });

            _disposable1 = ViewModel.Paste.Subscribe(async () =>
            {
                if (Application.Current?.Clipboard is IClipboard clipboard)
                {
                    string[] formats = await clipboard.GetFormatsAsync();

                    if (formats.AsSpan().Contains(Constants.Layer))
                    {
                        string json = await clipboard.GetTextAsync();
                        var layer = new Layer();
                        layer.ReadFromJson(JsonNode.Parse(json)!);
                        layer.Start = ViewModel.ClickedFrame;
                        layer.ZIndex = ViewModel.ClickedLayer;

                        layer.Save(Helper.RandomLayerFileName(Path.GetDirectoryName(ViewModel.Scene.FileName)!, Constants.LayerFileExtension));

                        ViewModel.Scene.AddChild(layer).DoAndRecord(CommandRecorder.Default);
                    }
                }
            });

            _disposable2 = ViewModel.EditorContext.SelectedObject.Subscribe(e =>
            {
                if (_selectedLayer != null)
                {
                    foreach (TimelineLayerViewModel item in ViewModel.Layers.GetMarshal().Value)
                    {
                        item.IsSelected.Value = false;
                    }

                    _selectedLayer = null;
                }

                if (e is Layer layer && FindLayerView(layer) is TimelineLayer { DataContext: TimelineLayerViewModel viewModel } newView)
                {
                    viewModel.IsSelected.Value = true;
                    _selectedLayer = newView;
                }
            });

            _disposable3 = ViewModel.EditorContext.Options.Subscribe(options =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Vector2 offset = options.Offset;
                    ContentScroll.Offset = new(offset.X, offset.Y);
                    PaneScroll.Offset = new(0, offset.Y);
                });
            });
        }
    }

    // PaneScrollがスクロールされた
    private void PaneScroll_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        ContentScroll.Offset = ContentScroll.Offset.WithY(PaneScroll.Offset.Y);
    }

    // マウスホイールが動いた
    private void ContentScroll_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        TimelineViewModel viewModel = ViewModel;
        Avalonia.Vector aOffset = ContentScroll.Offset;
        float scale = viewModel.Options.Value.Scale;
        var offset = new Vector2((float)aOffset.X, (float)aOffset.Y);

        if (e.KeyModifiers == KeyModifiers.Control)
        {
            // 目盛りのスケールを変更
            float oldScale = viewModel.Options.Value.Scale;
            TimeSpan ts = offset.X.ToTimeSpanF(oldScale);
            float deltaScale = (float)(e.Delta.Y / 10) * oldScale;
            scale = deltaScale + oldScale;

            offset.X = ts.ToPixelF(scale);
        }
        else if (e.KeyModifiers == KeyModifiers.Shift)
        {
            // オフセット(Y) をスクロール
            offset.Y -= (float)(e.Delta.X * 50);
        }
        else
        {
            // オフセット(X) をスクロール
            offset.X -= (float)(e.Delta.Y * 50);
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
        _pointerFrame = pointerPt.Position.X.ToTimeSpan(viewModel.Options.Value.Scale)
            .RoundToRate(viewModel.Scene.FindHierarchicalParent<Project>() is { } proj ? proj.GetFrameRate() : 30);

        if (ReferenceEquals(sender, TimelinePanel))
        {
            _pointerLayer = viewModel.ToLayerNumber(pointerPt.Position.Y);
        }

        if (_mouseFlag == MouseFlags.SeekBarPressed)
        {
            viewModel.Scene.CurrentFrame = _pointerFrame;
        }
        else if (_mouseFlag == MouseFlags.RangeSelectionPressed)
        {
            Rect rect = overlay.SelectionRange;
            overlay.SelectionRange = new(rect.Position, pointerPt.Position);
            UpdateRangeSelection();
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

            _mouseFlag = MouseFlags.Free;
        }
    }

    private void UpdateRangeSelection()
    {
        TimelineViewModel viewModel = ViewModel;
        foreach ((TimelineLayerViewModel layer, bool isSelectedOriginal) in _rangeSelection)
        {
            layer.IsSelected.Value = isSelectedOriginal;
        }

        _rangeSelection.Clear();
        Rect rect = overlay.SelectionRange.Normalize();
        var startTime = rect.Left.ToTimeSpan(viewModel.Options.Value.Scale);
        var endTime = rect.Right.ToTimeSpan(viewModel.Options.Value.Scale);
        var timeRange = TimeRange.FromRange(startTime, endTime);

        int startLayer = viewModel.ToLayerNumber(rect.Top);
        int endLayer = viewModel.ToLayerNumber(rect.Bottom);

        foreach (TimelineLayerViewModel item in viewModel.Layers)
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

        if (ReferenceEquals(sender, TimelinePanel))
        {
            viewModel.ClickedLayer = viewModel.ToLayerNumber(pointerPt.Position.Y);
        }

        TimelinePanel.Focus();

        if (pointerPt.Properties.IsLeftButtonPressed)
        {
            if (e.KeyModifiers == KeyModifiers.Control)
            {
                _mouseFlag = MouseFlags.RangeSelectionPressed;
                overlay.SelectionRange = new(pointerPt.Position, Size.Empty);
            }
            else
            {
                _mouseFlag = MouseFlags.SeekBarPressed;
                viewModel.Scene.CurrentFrame = viewModel.ClickedFrame;
            }
        }
    }

    // ポインターが離れた
    private void TimelinePanel_PointerExited(object? sender, PointerEventArgs e)
    {
        _mouseFlag = MouseFlags.Free;
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
        viewModel.ClickedLayer = viewModel.ToLayerNumber(pt.Y);

        if (e.Data.Get("SourceOperator") is OperatorRegistry.RegistryItem item2)
        {
            if (e.KeyModifiers == KeyModifiers.Control)
            {
                var dialog = new AddLayer
                {
                    DataContext = new AddLayerViewModel(scene, new LayerDescription(viewModel.ClickedFrame, TimeSpan.FromSeconds(5), viewModel.ClickedLayer, InitialOperator: item2))
                };
                await dialog.ShowAsync();
            }
            else
            {
                viewModel.AddLayer.Execute(new LayerDescription(
                    viewModel.ClickedFrame, TimeSpan.FromSeconds(5), viewModel.ClickedLayer, InitialOperator: item2));
            }
        }
    }

    private void TimelinePanel_DragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains("SourceOperator")
            || (e.Data.GetFileNames()?.Any() ?? false))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    // レイヤーを追加
    private async void AddLayerClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new AddLayer
        {
            DataContext = new AddLayerViewModel(ViewModel.Scene,
                new LayerDescription(ViewModel.ClickedFrame, TimeSpan.FromSeconds(5), ViewModel.ClickedLayer))
        };
        await dialog.ShowAsync();
    }

    private async void ShowSceneSettings(object? sender, RoutedEventArgs e)
    {
        var dialog = new SceneSettings()
        {
            DataContext = new SceneSettingsViewModel(ViewModel.Scene)
        };
        await dialog.ShowAsync();
    }

    // レイヤーを追加
    private void AddLayer(int index, TimelineLayerViewModel viewModel)
    {
        var view = new TimelineLayer
        {
            DataContext = viewModel
        };

        TimelinePanel.Children.Add(view);
    }

    // レイヤーを削除
    private void RemoveLayer(int index, TimelineLayerViewModel viewModel)
    {
        Layer layer = viewModel.Model;

        for (int i = 0; i < TimelinePanel.Children.Count; i++)
        {
            IControl item = TimelinePanel.Children[i];
            if (item.DataContext is TimelineLayerViewModel vm && vm.Model == layer)
            {
                TimelinePanel.Children.RemoveAt(i);
                break;
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
            IControl item = TimelinePanel.Children[i];
            if (item.DataContext is InlineAnimationLayerViewModel vm && vm.Property == prop)
            {
                TimelinePanel.Children.RemoveAt(i);
                break;
            }
        }
    }

    private TimelineLayer? FindLayerView(Layer layer)
    {
        return TimelinePanel.Children.FirstOrDefault(ctr => ctr.DataContext is TimelineLayerViewModel vm && vm.Model == layer) as TimelineLayer;
    }

    private void ZoomClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is MenuItem menuItem)
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
}
