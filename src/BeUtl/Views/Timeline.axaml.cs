using System.Collections.Specialized;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Remote.Protocol.Input;
using Avalonia.VisualTree;
using BeUtl.Collections;
using BeUtl.Models;
using BeUtl.ProjectSystem;
using BeUtl.ViewModels;
using BeUtl.ViewModels.Dialogs;
using BeUtl.Views.Dialogs;
using FluentAvalonia.UI.Controls;

namespace BeUtl.Views;

public partial class Timeline : UserControl
{
    internal enum MouseFlags
    {
        MouseUp,
        MouseDown
    }

    internal MouseFlags _seekbarMouseFlag = MouseFlags.MouseUp;
    private TimeSpan _clickedFrame;
    private int _clickedLayer;
    internal TimeSpan _pointerFrame;
    internal int _pointerLayer;
    private bool _isFirst = true;
    private TimelineViewModel? _viewModel;

    public Timeline()
    {
        InitializeComponent();

        ContentScroll.ScrollChanged += ContentScroll_ScrollChanged;
        ContentScroll.AddHandler(PointerWheelChangedEvent, ContentScroll_PointerWheelChanged, RoutingStrategies.Tunnel);
        ScaleScroll.AddHandler(PointerWheelChangedEvent, ContentScroll_PointerWheelChanged, RoutingStrategies.Tunnel);

        TimelinePanel.AddHandler(DragDrop.DragOverEvent, TimelinePanel_DragOver);
        TimelinePanel.AddHandler(DragDrop.DropEvent, TimelinePanel_Drop);
        DragDrop.SetAllowDrop(TimelinePanel, true);
    }

    internal TimelineViewModel ViewModel => _viewModel!;

    // DataContextが変更された
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is TimelineViewModel vm && vm != _viewModel)
        {
            if (_viewModel != null)
            {
                TimelinePanel.Children.RemoveRange(3, TimelinePanel.Children.Count - 3);

                _viewModel.Scene.Children.CollectionChanged -= Children_CollectionChanged;
            }

            _viewModel = vm;

            var minHeightBinding = new Binding("TimelineOptions")
            {
                Source = ViewModel.Scene,
                Converter = new FuncValueConverter<TimelineOptions, double>(x => x.MaxLayerCount * Helper.LayerHeight)
            };
            TimelinePanel[!MinHeightProperty] = minHeightBinding;
            LeftPanel[!MinHeightProperty] = minHeightBinding;

            ViewModel.Scene.Children.CollectionChanged += Children_CollectionChanged;
            AddLayers(ViewModel.Scene.Children);
        }
    }

    // PaneScrollがスクロールされた
    private void PaneScroll_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        ContentScroll.Offset = ContentScroll.Offset.WithY(PaneScroll.Offset.Y);
    }

    // ContentScrollがスクロールされた
    private void ContentScroll_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        Scene scene = ViewModel.Scene;
        if (_isFirst)
        {
            ContentScroll.Offset = new(scene.TimelineOptions.Offset.X, scene.TimelineOptions.Offset.Y);
            PaneScroll.Offset = new(0, scene.TimelineOptions.Offset.Y);

            _isFirst = false;
        }

        scene.TimelineOptions = scene.TimelineOptions with
        {
            Offset = new Vector2((float)ContentScroll.Offset.X, (float)ContentScroll.Offset.Y)
        };

        ScaleScroll.Offset = new(ContentScroll.Offset.X, 0);
        PaneScroll.Offset = PaneScroll.Offset.WithY(ContentScroll.Offset.Y);
    }

    // マウスホイールが動いた
    private void ContentScroll_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        Scene scene = ViewModel.Scene;
        Avalonia.Vector offset = ContentScroll.Offset;

        if (e.KeyModifiers == KeyModifiers.Control)
        {
            // 目盛りのスケールを変更
            float scale = scene.TimelineOptions.Scale;
            var ts = offset.X.ToTimeSpan(scale);
            float deltaScale = (float)(e.Delta.Y / 120) * 10 * scale;
            scene.TimelineOptions = scene.TimelineOptions with
            {
                Scale = deltaScale + scale,
            };

            offset = offset.WithX(ts.ToPixel(scene.TimelineOptions.Scale));
        }
        else if (e.KeyModifiers == KeyModifiers.Shift)
        {
            // オフセット(Y) をスクロール
            offset = offset.WithY(offset.Y - (e.Delta.Y * 50));
        }
        else
        {
            // オフセット(X) をスクロール
            offset = offset.WithX(offset.X - (e.Delta.Y * 50));
        }

        ContentScroll.Offset = offset;
        e.Handled = true;
    }

    // ポインター移動
    private void TimelinePanel_PointerMoved(object? sender, PointerEventArgs e)
    {
        PointerPoint pointerPt = e.GetCurrentPoint(TimelinePanel);
        _pointerFrame = pointerPt.Position.X.ToTimeSpan(ViewModel.Scene.TimelineOptions.Scale);
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
        _clickedFrame = pointerPt.Position.X.ToTimeSpan(ViewModel.Scene.TimelineOptions.Scale);
        _clickedLayer = pointerPt.Position.Y.ToLayerNumber();

        if (pointerPt.Properties.IsLeftButtonPressed)
        {
            _seekbarMouseFlag = MouseFlags.MouseDown;
            ViewModel.Scene.CurrentFrame = _clickedFrame;
        }
    }

    // ポインターが離れた
    private void TimelinePanel_PointerLeave(object? sender, PointerEventArgs e)
    {
        _seekbarMouseFlag = MouseFlags.MouseUp;
    }

    // ドロップされた
    private async void TimelinePanel_Drop(object? sender, DragEventArgs e)
    {
        TimelinePanel.Cursor = Cursors.Arrow;
        Scene scene = ViewModel.Scene;
        Point pt = e.GetPosition(TimelinePanel);

        _clickedFrame = pt.X.ToTimeSpan(scene.TimelineOptions.Scale);
        _clickedLayer = pt.Y.ToLayerNumber();

        if (e.Data.Get("RenderOperation") is RenderOperationRegistry.RegistryItem item)
        {
            if (e.KeyModifiers == KeyModifiers.Control)
            {
                var dialog = new AddLayer
                {
                    DataContext = new AddLayerViewModel(scene, new LayerDescription(_clickedFrame, TimeSpan.FromSeconds(5), _clickedLayer, item))
                };
                await dialog.ShowAsync();
            }
            else
            {
                ViewModel.AddLayer.Execute(new LayerDescription(
                    _clickedFrame, TimeSpan.FromSeconds(5), _clickedLayer, item));
            }
        }
    }

    private void TimelinePanel_DragOver(object? sender, DragEventArgs e)
    {
        TimelinePanel.Cursor = Cursors.DragCopy;
        e.DragEffects = e.Data.Contains("RenderOperation") || (e.Data.GetFileNames()?.Any() ?? false) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    // Scene.Childrenが変更された
    private void Children_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            AddLayers(e.NewItems.OfType<Layer>());
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
        {
            RemoveLayers(e.OldItems.OfType<Layer>());
        }
    }

    // レイヤーを追加
    private async void AddLayerClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new AddLayer
        {
            DataContext = new AddLayerViewModel(ViewModel.Scene,
                new LayerDescription(_clickedFrame, TimeSpan.FromSeconds(5), _clickedLayer))
        };
        await dialog.ShowAsync();
    }

    // ペースト
    private void PasteClick(object? sender, RoutedEventArgs e)
    {
    }

    // レイヤーを追加
    private void AddLayers(IEnumerable<Layer> items)
    {
        foreach (Layer layer in items)
        {
            var viewModel = new TimelineLayerViewModel(layer);
            var view = new TimelineLayer
            {
                DataContext = viewModel
            };

            TimelinePanel.Children.Add(view);

            LeftPanel.Children.Add(new LayerHeader
            {
                DataContext = viewModel
            });
        }
    }

    // レイヤーを削除
    private void RemoveLayers(IEnumerable<Layer> items)
    {
        foreach (Layer layer in items)
        {
            for (int i = 0; i < TimelinePanel.Children.Count; i++)
            {
                IControl item = TimelinePanel.Children[i];
                if (item.DataContext is TimelineLayerViewModel vm && vm.Model == layer)
                {
                    TimelinePanel.Children.RemoveAt(i);
                }
            }

            for (int i = 0; i < LeftPanel.Children.Count; i++)
            {
                IControl item = LeftPanel.Children[i];
                if (item.DataContext is TimelineLayerViewModel vm && vm.Model == layer)
                {
                    LeftPanel.Children.RemoveAt(i);
                }
            }
        }
    }
}
