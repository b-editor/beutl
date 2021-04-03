using System;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using BEditor.Data;
using BEditor.Extensions;
using BEditor.Properties;
using BEditor.ViewModels.Timelines;

using Reactive.Bindings.Extensions;

namespace BEditor.Views.Timelines
{
    public class Timeline : UserControl
    {
        private readonly ScrollViewer _scrollLine;
        private readonly ScrollViewer _scrollLabel;
        private readonly StackPanel _layerLabel;
        private readonly Grid _scale;
        private readonly StackPanel _layer;
        private readonly Grid _timelineGrid;
        private readonly ContextMenu _timelineMenu;

        public Timeline()
        {
            InitializeComponent();

            _scrollLine = this.FindControl<ScrollViewer>("ScrollLine");
            _scrollLabel = this.FindControl<ScrollViewer>("ScrollLabel");
            _layerLabel = this.FindControl<VirtualizingStackPanel>("LayerLabel");
            _scale = this.FindControl<Grid>("scale");
            _layer = this.FindControl<VirtualizingStackPanel>("Layer");
            _timelineGrid = this.FindControl<Grid>("timelinegrid");
            _timelineMenu = this.FindControl<ContextMenu>("TimelineMenu");

            throw new Exception();
        }

        public Timeline(Scene scene)
        {
            ContextMenu CreateMenu(int layer)
            {
                var contextMenu = new ContextMenu();

                var remove = new MenuItem();

                remove.SetValue(AttachmentProperty.IntProperty, layer);
                remove.Header = new VirtualizingStackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new PathIcon
                        {
                            Data= Geometry.Parse("M24 6.75C27.3751 6.75 30.1253 9.42524 30.2459 12.7709L30.25 13.001L37 13C37.9665 13 38.75 13.7835 38.75 14.75C38.75 15.6682 38.0429 16.4212 37.1435 16.4942L37 16.5H35.833L34.2058 38.0698C34.0385 40.2867 32.191 42 29.9679 42H18.0321C15.809 42 13.9615 40.2867 13.7942 38.0698L12.166 16.5H11C10.0818 16.5 9.32881 15.7929 9.2558 14.8935L9.25 14.75C9.25 13.8318 9.95711 13.0788 10.8565 13.0058L11 13H17.75C17.75 9.70163 20.305 7.00002 23.5438 6.76639L23.7709 6.75412L24 6.75ZM27.75 19.75C27.1028 19.75 26.5705 20.2419 26.5065 20.8722L26.5 21V33L26.5065 33.1278C26.5705 33.7581 27.1028 34.25 27.75 34.25C28.3972 34.25 28.9295 33.7581 28.9935 33.1278L29 33V21L28.9935 20.8722C28.9295 20.2419 28.3972 19.75 27.75 19.75ZM20.25 19.75C19.6028 19.75 19.0705 20.2419 19.0065 20.8722L19 21V33L19.0065 33.1278C19.0705 33.7581 19.6028 34.25 20.25 34.25C20.8972 34.25 21.4295 33.7581 21.4935 33.1278L21.5 33V21L21.4935 20.8722C21.4295 20.2419 20.8972 19.75 20.25 19.75ZM24.1675 10.255L24 10.25C22.5375 10.25 21.3416 11.3917 21.255 12.8325L21.25 13.001L26.75 13C26.75 11.5375 25.6083 10.3416 24.1675 10.255Z"),
                            Margin = new Thickness(5, 0, 5, 0)
                        },
                        new TextBlock
                        {
                            Text = Strings.Remove,
                            Margin = new Thickness(20, 0, 5, 0)
                        }
                    }
                };

                contextMenu.Items = new MenuItem[] { remove };

                remove.Click += (s, _) =>
                {
                    var menu = (MenuItem)s!;
                    var layer = menu.GetValue(AttachmentProperty.IntProperty);

                    Scene.RemoveLayer(layer).Execute();
                };

                return contextMenu;
            }

            var viewmodel = scene.GetCreateTimelineViewModel();
            DataContext = viewmodel;
            InitializeComponent();

            _scrollLine = this.FindControl<ScrollViewer>("ScrollLine");
            _scrollLabel = this.FindControl<ScrollViewer>("ScrollLabel");
            _layerLabel = this.FindControl<StackPanel>("LayerLabel");
            _scale = this.FindControl<Grid>("scale");
            _layer = this.FindControl<StackPanel>("Layer");
            _timelineGrid = this.FindControl<Grid>("timelinegrid");
            _timelineMenu = this.FindControl<ContextMenu>("TimelineMenu");

            InitializeContextMenu();


            // レイヤー名追加for
            for (var l = 1; l < 100; l++)
            {
                var trackHeight_bind = new Binding("TrackHeight");

                var track = new Grid();

                var grid = new Grid
                {
                    Margin = new Thickness(0, 1, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                };

                grid.SetValue(AttachmentProperty.IntProperty, l);
                grid.Bind(WidthProperty, new Binding("TrackWidth.Value") { Mode = BindingMode.OneWay });
                grid.Bind(HeightProperty, trackHeight_bind);
                //SystemControlBackgroundChromeBlackLowBrush
                #region Eventの設定

                grid.PointerPressed += (s, e) =>
                {
                    var grid = (Grid)s!;

                    ViewModel.LayerSelect.Execute((grid.GetValue(AttachmentProperty.IntProperty), scene.ToFrame(e.GetPosition(grid).X)));
                };
                //grid.PreviewDrop += (s, e) => ViewModel.LayerDropCommand.Execute((s, e));
                grid.PointerMoved += (s, e) => ViewModel.LayerMove.Execute(s);
                //grid.PreviewDragOver += (s, e) => ViewModel.LayerDragOverCommand.Execute((s, e));

                #endregion

                track.Children.Add(grid);
                _layer.Children.Add(track);

                #region レイヤー数

                {
                    var binding = new Binding("Bounds.Height")
                    {
                        Source = track
                    };

                    var layer_row = new Grid
                    {
                        ContextMenu = CreateMenu(l),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Width = 200,
                    };

                    layer_row.Bind(HeightProperty, binding);

                    var toggle = new ToggleButton
                    {
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Top,
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        Padding = new(0),
                        Background = null,
                        Content = l,
                        Width = 200
                    };
                    toggle.Bind(HeightProperty, trackHeight_bind);
                    layer_row.Children.Add(toggle);

                    toggle.Click += (s, _) =>
                    {
                        var toggle = (ToggleButton)s!;
                        var l = (int)toggle.Content;

                        if (!(bool)toggle.IsChecked!)
                        {
                            Scene.HideLayer.Remove(l);
                        }
                        else
                        {
                            Scene.HideLayer.Add(l);
                        }

                        Scene.Parent!.PreviewUpdate();
                    };

                    if (Scene.HideLayer.Exists(x => x == l))
                    {
                        toggle.IsChecked = true;
                    }

                    _layerLabel.Children.Add(layer_row);
                }

                #endregion
            }

            //viewmodel.ResetScale = AddScale;
            //viewmodel.ClipLayerMoveCommand = ClipLayerMove;
            //viewmodel.GetLayerMousePosition = () => Mouse.GetPosition(Layer);

            Scene.Datas.CollectionChanged += ClipsCollectionChanged;
            _scrollLine.ScrollChanged += ScrollLine_ScrollChanged1;
            _scrollLabel.ScrollChanged += ScrollLabel_ScrollChanged;
            _timelineGrid.PointerMoved += TimelineGrid_PointerMoved;
            _timelineGrid.PointerReleased += TimelineGrid_PointerReleased;
            _timelineGrid.PointerPressed += TimelineGrid_PointerPressed;
            _timelineGrid.PointerLeave += TimelineGrid_PointerLeave;

            // WPF の Preview* イベント
            _scrollLine.AddHandler(PointerWheelChangedEvent, ScrollLine_PointerWheel, RoutingStrategies.Tunnel);

            // 
            viewmodel.ResetScale = (zoom, max, rate) =>
            {
            };
            viewmodel.ClipLayerMoveCommand = (clip, layer) =>
            {
                var vm = clip.GetCreateClipViewModel();
                var from = vm.Row;
                vm.Row = layer;

                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var togrid = (Grid)_layer.Children[layer];

                    var ui = clip.GetCreateClipView();
                    (ui.Parent as Grid)?.Children?.Remove(ui);

                    togrid.Children.Add(ui);
                });
            };
        }

        private TimelineViewModel ViewModel => (TimelineViewModel)DataContext!;
        private Scene Scene => ViewModel.Scene;

        protected override void OnInitialized()
        {
            base.OnInitialized();

            // クリップを追加
            for (var i = 0; i < Scene.Datas.Count; i++)
            {
                var clip = Scene.Datas[i];

                var grid = (Grid)_layer.Children[clip.Layer];

                grid.Children.Add(clip.GetCreateClipView());
            }

            Scene.ObserveProperty(s => s.TimeLineZoom)
                .Subscribe(_ =>
                {
                    var viewmodel = ViewModel;
                    var scene = viewmodel.Scene;

                    if (scene.TimeLineZoom <= 0)
                    {
                        scene.TimeLineZoom = 1;
                        return;
                    }
                    if (scene.TimeLineZoom >= 201)
                    {
                        scene.TimeLineZoom = 200;
                        return;
                    }

                    var l = scene.TotalFrame;

                    viewmodel.TrackWidth.Value = scene.ToPixel(l);

                    for (var index = 0; index < scene.Datas.Count; index++)
                    {
                        var info = scene.Datas[index];
                        var start = scene.ToPixel(info.Start);
                        var length = scene.ToPixel(info.Length);

                        var vm = info.GetCreateClipViewModelSafe();
                        vm.MarginProperty.Value = new Thickness(start, 1, 0, 0);
                        vm.WidthProperty.Value = length;
                    }

                    viewmodel.SeekbarMargin.Value = new Thickness(scene.ToPixel(scene.PreviewFrame), 0, 0, 0);

                    viewmodel.ResetScale?.Invoke(scene.TimeLineZoom, scene.TotalFrame, scene.Parent.Framerate);
                });
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void InitializeContextMenu()
        {
            var clipMenu = new MenuItem
            {
                Header = Strings.AddClip,
                Items = ObjectMetadata.LoadedObjects.Select(metadata => new MenuItem
                {
                    CommandParameter = metadata,
                    Command = ViewModel.AddClip,
                    Header = metadata.Name
                }).ToArray()
            };

            if (_timelineMenu.Items is AvaloniaList<object> list)
            {
                list.Insert(0, clipMenu);
                list.Insert(1, new Separator());
            }
        }

        private void TimelineGrid_PointerLeave(object? sender, PointerEventArgs e)
        {
            ViewModel.TimelinePointerLeaved.Execute();
        }

        private void TimelineGrid_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var visual = (IVisual?)sender;
            var point = e.GetCurrentPoint(visual);

            if (point.Properties.IsLeftButtonPressed)
            {
                ViewModel.TimelinePointerLeftPressed.Execute(point.Position);
            }
        }

        private void TimelineGrid_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            var visual = (IVisual?)sender;
            var point = e.GetCurrentPoint(visual);

            if (point.Properties.PointerUpdateKind is PointerUpdateKind.LeftButtonReleased)
            {
                ViewModel.TimelinePointerLeftReleased.Execute();
            }
        }

        private void TimelineGrid_PointerMoved(object? sender, PointerEventArgs e)
        {
            ViewModel.TimelinePointerMoved.Execute(e.GetPosition((IVisual?)sender));
        }

        private void ScrollLine_ScrollChanged1(object? sender, ScrollChangedEventArgs e)
        {
            _scrollLabel.Offset = _scrollLabel.Offset.WithY(_scrollLine.Offset.Y);
        }

        private void ScrollLabel_ScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            _scrollLine.Offset = _scrollLine.Offset.WithY(_scrollLabel.Offset.Y);
        }

        private async void ClipsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (e.Action == NotifyCollectionChangedAction.Add)
                {
                    var item = Scene.Datas[e.NewStartingIndex];

                    var grid = (Grid)_layer.Children[item.Layer];
                    
                    grid.Children.Add(item.GetCreateClipViewSafe());
                }
                else if (e.Action == NotifyCollectionChangedAction.Remove)
                {
                    var item = e.OldItems![0];

                    if (item is ClipElement clip)
                    {
                        var view = clip.GetCreateClipViewSafe();
                        (view.Parent as Grid)?.Children?.Remove(view);

                        clip.Clear();

                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                    }
                }
            });
        }

        public void ScrollLine_PointerWheel(object? sender, PointerWheelEventArgs e)
        {
            if (e.KeyModifiers is KeyModifiers.Control)
            {
                if (!(Scene.TimeLineZoom > 200 || Scene.TimeLineZoom < 1))
                {
                    var offset = _scrollLine.Offset.X;
                    var frame = Scene.ToFrame(offset);
                    Scene.TimeLineZoom += (float)(e.Delta.Y / 120) * 5;

                    _scrollLine.Offset = _scrollLine.Offset.WithX(Scene.ToPixel(frame));
                }
            }
            else if (e.Delta.Y > 0)
            {
                for (var i = 0; i < 4; i++)
                {
                    _scrollLine.LineLeft();
                }
            }
            else
            {
                for (var i = 0; i < 4; i++)
                {
                    _scrollLine.LineRight();
                }
            }

            e.Handled = true;
        }

        public void ScrollLine_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            Scene.TimeLineHorizonOffset = _scrollLine.Offset.X;
            Scene.TimeLineVerticalOffset = _scrollLine.Offset.Y;
        }
    }
}
