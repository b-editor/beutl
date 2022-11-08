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

using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Streaming;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.Views.Dialogs;

namespace Beutl.Views;

public sealed partial class Timeline : UserControl
{
    internal enum MouseFlags
    {
        MouseUp,
        MouseDown
    }

    internal MouseFlags _seekbarMouseFlag = MouseFlags.MouseUp;
    internal TimeSpan _pointerFrame;
    internal int _pointerLayer;
    private TimelineViewModel? _viewModel;
    private IDisposable? _disposable0;
    private IDisposable? _disposable1;
    private IDisposable? _disposable2;
    private IDisposable? _disposable3;
    private TimelineLayer? _selectedLayer;

    public Timeline()
    {
        InitializeComponent();

        gridSplitter.DragDelta += GridSplitter_DragDelta;

        ContentScroll.AddHandler(PointerWheelChangedEvent, ContentScroll_PointerWheelChanged, RoutingStrategies.Tunnel);
        ScaleScroll.AddHandler(PointerWheelChangedEvent, ContentScroll_PointerWheelChanged, RoutingStrategies.Tunnel);

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
                TimelinePanel.Children.RemoveRange(3, TimelinePanel.Children.Count - 3);

                _disposable0?.Dispose();
                _disposable1?.Dispose();
                _disposable2?.Dispose();
                _disposable3?.Dispose();
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
                    _selectedLayer.border.BorderThickness = new Thickness(0);
                    _selectedLayer = null;
                }

                if (e is Layer layer && FindLayerView(layer) is TimelineLayer newView)
                {
                    newView.border.BorderThickness = new Thickness(1);
                    _selectedLayer = newView;
                }
            });

            _disposable3 = ViewModel.EditorContext.Options.Subscribe(options =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Vector2 offset = options.Offset;
                    ScaleScroll.Offset = new(offset.X, 0);
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
            float deltaScale = (float)(e.Delta.Y / 120) * 10 * oldScale;
            scale = deltaScale + oldScale;

            offset.X = ts.ToPixelF(scale);
        }
        else if (e.KeyModifiers == KeyModifiers.Shift)
        {
            // オフセット(Y) をスクロール
            offset.Y -= (float)(e.Delta.Y * 50);
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
        PointerPoint pointerPt = e.GetCurrentPoint(TimelinePanel);
        _pointerFrame = pointerPt.Position.X.ToTimeSpan(ViewModel.Options.Value.Scale)
            .RoundToRate(ViewModel.Scene.Parent is Project proj ? proj.GetFrameRate() : 30);
        _pointerLayer = pointerPt.Position.Y.ToLayerNumber();

        if (_seekbarMouseFlag == MouseFlags.MouseDown)
        {
            ViewModel.Scene.CurrentFrame = _pointerFrame;
        }
    }

    // ポインターが放された
    private void TimelinePanel_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        PointerPoint pointerPt = e.GetCurrentPoint(TimelinePanel);

        if (pointerPt.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased)
        {
            _seekbarMouseFlag = MouseFlags.MouseUp;
        }
    }

    // ポインターが押された
    private void TimelinePanel_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint pointerPt = e.GetCurrentPoint(TimelinePanel);
        ViewModel.ClickedFrame = pointerPt.Position.X.ToTimeSpan(ViewModel.Options.Value.Scale)
            .RoundToRate(ViewModel.Scene.Parent is Project proj ? proj.GetFrameRate() : 30);
        ViewModel.ClickedLayer = pointerPt.Position.Y.ToLayerNumber();
        TimelinePanel.Focus();

        if (pointerPt.Properties.IsLeftButtonPressed)
        {
            _seekbarMouseFlag = MouseFlags.MouseDown;
            ViewModel.Scene.CurrentFrame = ViewModel.ClickedFrame;
        }
    }

    // ポインターが離れた
    private void TimelinePanel_PointerExited(object? sender, PointerEventArgs e)
    {
        _seekbarMouseFlag = MouseFlags.MouseUp;
    }

    // ドロップされた
    private async void TimelinePanel_Drop(object? sender, DragEventArgs e)
    {
        TimelinePanel.Cursor = Cursors.Arrow;
        Scene scene = ViewModel.Scene;
        Point pt = e.GetPosition(TimelinePanel);

        ViewModel.ClickedFrame = pt.X.ToTimeSpan(ViewModel.Options.Value.Scale)
            .RoundToRate(ViewModel.Scene.Parent is Project proj ? proj.GetFrameRate() : 30);
        ViewModel.ClickedLayer = pt.Y.ToLayerNumber();

        if (e.Data.Get("StreamOperator") is OperatorRegistry.RegistryItem item2)
        {
            if (e.KeyModifiers == KeyModifiers.Control)
            {
                var dialog = new AddLayer
                {
                    DataContext = new AddLayerViewModel(scene, new LayerDescription(ViewModel.ClickedFrame, TimeSpan.FromSeconds(5), ViewModel.ClickedLayer, InitialOperator: item2))
                };
                await dialog.ShowAsync();
            }
            else
            {
                ViewModel.AddLayer.Execute(new LayerDescription(
                    ViewModel.ClickedFrame, TimeSpan.FromSeconds(5), ViewModel.ClickedLayer, InitialOperator: item2));
            }
        }
    }

    private void TimelinePanel_DragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains("RenderOperation")
            || e.Data.Contains("StreamOperator")
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

    private TimelineLayer? FindLayerView(Layer layer)
    {
        return TimelinePanel.Children.FirstOrDefault(ctr => ctr.DataContext is TimelineLayerViewModel vm && vm.Model == layer) as TimelineLayer;
    }
}
