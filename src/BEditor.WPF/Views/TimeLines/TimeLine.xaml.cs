using System;
using System.Collections.Specialized;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

using BEditor.Command;
using BEditor.Data;
using BEditor.Models;
using BEditor.Models.Extension;
using BEditor.ViewModels;
using BEditor.ViewModels.TimeLines;

using MaterialDesignThemes.Wpf;

using Resource = BEditor.Properties.Resources;

namespace BEditor.Views.TimeLines
{
    /// <summary>
    /// TimeLine.xaml の相互作用ロジック
    /// </summary>
    public sealed partial class TimeLine : UserControl
    {
        private readonly TimeLineViewModel _viewModel;
        private bool _isFirstLoad = true;

        public TimeLine(Scene scene)
        {
            ContextMenu CreateMenu(int layer)
            {
                var contextMenu = new ContextMenu();

                #region 削除

                var remove = new MenuItem();

                AttachmentProperty.SetInt(remove, layer);
                var removeMenu = new VirtualizingStackPanel()
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new PackIcon() { Kind = PackIconKind.Delete, Margin = new Thickness(5, 0, 5, 0) },
                        new TextBlock() { Text = Resource.Remove, Margin = new Thickness(20, 0, 5, 0) }
                    }
                };
                remove.Header = removeMenu;

                contextMenu.Items.Add(remove);

                remove.Click += (s, _) =>
                {
                    var menu = (MenuItem)s;
                    var layer = (int)menu.GetValue(AttachmentProperty.IntProperty);

                    Scene.RemoveLayer(layer).Execute();
                };

                #endregion

                return contextMenu;
            }

            DataContext = _viewModel = scene.GetCreateTimeLineViewModel();

            InitializeComponent();
            InitializeContextMenu();

            // レイヤー名追加for
            for (int l = 1; l < 100; l++)
            {
                var trackHeight_bind = new Binding("TrackHeight");

                var track = new Grid();

                var grid = new Grid()
                {
                    Margin = new Thickness(0, 1, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    AllowDrop = true,
                    VerticalAlignment = VerticalAlignment.Top
                };

                grid.SetValue(AttachmentProperty.IntProperty, l);
                grid.SetBinding(WidthProperty, new Binding("TrackWidth.Value") { Mode = BindingMode.OneWay });
                grid.SetResourceReference(BackgroundProperty, "MaterialDesignCardBackground");
                grid.SetBinding(HeightProperty, trackHeight_bind);

                #region Eventの設定

                // Interaction.GetTriggersを使うなら普通にAddHandlerしたい。

                grid.MouseDown += (s, _) => _viewModel.LayerSelectCommand.Execute(s);
                grid.PreviewDrop += (s, e) => _viewModel.LayerDropCommand.Execute((s, e));
                grid.MouseMove += (s, e) => _viewModel.LayerMoveCommand.Execute(s);
                grid.PreviewDragOver += (s, e) => _viewModel.LayerDragOverCommand.Execute((s, e));

                #endregion

                track.Children.Add(grid);
                Layer.Children.Add(track);

                #region レイヤー数

                {
                    var binding = new Binding("ActualHeight")
                    {
                        Source = track
                    };

                    var layer_row = new Grid
                    {
                        ContextMenu = CreateMenu(l),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Width = 200,
                    };

                    layer_row.SetBinding(HeightProperty, binding);

                    var toggle = new ToggleButton()
                    {
                        Style = (Style)Resources["TimelineHideShowToggleButton"],
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Top,
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        Content = l,
                        Width = 200
                    };
                    toggle.SetBinding(HeightProperty, trackHeight_bind);
                    layer_row.Children.Add(toggle);

                    toggle.Click += (s, _) =>
                    {
                        var toggle = (ToggleButton)s;
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

                    LayerLabel.Children.Add(layer_row);
                }

                #endregion
            }

            ScrollLabel.ScrollToVerticalOffset(Scene.TimeLineVerticalOffset);
            ScrollLine.ScrollToVerticalOffset(Scene.TimeLineVerticalOffset);
            ScrollLine.ScrollToHorizontalOffset(Scene.TimeLineHorizonOffset);
        }


        private Scene Scene => _viewModel.Scene;


        private void InitializeContextMenu()
        {
            var clipMenu = new MenuItem()
            {
                Header = Resource.AddClip
            };
            foreach (var objmetadata in ObjectMetadata.LoadedObjects)
            {
                var menu = new MenuItem
                {
                    DataContext = objmetadata,
                    Command = _viewModel.AddClip
                };

                menu.SetBinding(MenuItem.CommandParameterProperty, new Binding());
                menu.SetBinding(HeaderedItemsControl.HeaderProperty, new Binding("Name") { Mode = BindingMode.OneTime });
                clipMenu.Items.Add(menu);
            }
            TimelineMenu.Items.Insert(0, clipMenu);
            TimelineMenu.Items.Insert(1, new Separator());
        }

        private void ScrollLine_ScrollChanged1(object sender, ScrollChangedEventArgs e)
        {
            ScrollLabel.ScrollToVerticalOffset(ScrollLine.VerticalOffset);
        }
        private void ScrollLabel_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            ScrollLine.ScrollToVerticalOffset(ScrollLabel.VerticalOffset);
        }
        private void ScrollLine_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollviewer = (ScrollViewer)sender;

            if (Keyboard.IsKeyDown(Key.LeftCtrl))
            {
                if (!(Scene.TimeLineZoom > 200 || Scene.TimeLineZoom < 1))
                {
                    var offset = scrollviewer.HorizontalOffset;
                    var frame = _viewModel.ToFrame(offset);
                    Scene.TimeLineZoom += (e.Delta / 120) * 5;

                    scrollviewer.ScrollToHorizontalOffset(_viewModel.ToPixel(frame));
                }
            }
            else
            {
                if (e.Delta > 0)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        scrollviewer.LineLeft();
                    }
                }
                else
                {
                    for (int i = 0; i < 4; i++)
                    {
                        scrollviewer.LineRight();
                    }
                }
            }

            e.Handled = true;
        }

        private void AddScale(float zoom, int max, int rate)
        {
            Dispatcher.InvokeAsync(() =>
            {
                int top = 16;//15
                double ToPixel(int frame)
                {
                    return Setting.WidthOf1Frame * (zoom / 200) * frame;
                }

                double SecToPixel(float sec)
                {
                    return ToPixel((int)(sec * rate));
                }

                double MinToPixel(float min)
                {
                    return SecToPixel(min * 60);
                }

                scale.Children.Clear();
                //max 1000
                if (zoom <= 200 && zoom >= 100)
                {
                    //sは秒数
                    for (int s = 0; s < (max / rate); s++)
                    {
                        //一秒毎
                        var border = new Border
                        {
                            Width = 1,
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Stretch,

                            Margin = new Thickness(ToPixel(s * rate - 1), 5, 0, 0)
                        };
                        border.SetResourceReference(BackgroundProperty, "MaterialDesignBody");
                        scale.Children.Add(border);
                        if (s is not 0)
                        {
                            scale.Children.Add(new TextBlock()
                            {
                                Margin = new Thickness(ToPixel(s * rate + 1), 0, 0, 0),
                                HorizontalAlignment = HorizontalAlignment.Left,
                                VerticalAlignment = VerticalAlignment.Top,
                                Text = s.ToString() + " sec"
                            });
                        }

                        //以下はフレーム
                        if (zoom <= 200 && zoom >= 166.7)
                        {
                            for (int m = 1; m < rate; m++)
                            {
                                var border2 = new Border
                                {
                                    Width = 1,
                                    HorizontalAlignment = HorizontalAlignment.Left,

                                    Margin = new Thickness(ToPixel(s * rate - 1 + m), top, 0, 0)
                                };

                                border2.SetResourceReference(BackgroundProperty, "MaterialDesignBodyLight");
                                scale.Children.Add(border2);
                            }
                        }
                        else if (zoom < 166.7 && zoom >= 133.4)
                        {
                            for (int m = 1; m < rate / 2; m++)
                            {
                                var border2 = new Border
                                {
                                    Width = 1,
                                    HorizontalAlignment = HorizontalAlignment.Left,

                                    Margin = new Thickness(ToPixel(s * rate - 1 + m * 2), top, 0, 0)
                                };

                                border2.SetResourceReference(BackgroundProperty, "MaterialDesignBodyLight");
                                scale.Children.Add(border2);
                            }
                        }
                        else if (zoom < 133.4 && zoom >= 100)
                        {
                            for (int m = 1; m < rate / 4; m++)
                            {
                                var border2 = new Border
                                {
                                    Width = 1,
                                    HorizontalAlignment = HorizontalAlignment.Left,

                                    Margin = new Thickness(ToPixel(s * rate - 1 + m * 4), top, 0, 0)
                                };

                                border2.SetResourceReference(BackgroundProperty, "MaterialDesignBodyLight");
                                scale.Children.Add(border2);
                            }
                        }
                    }
                }
                else
                {
                    //m は分数
                    //最大の分
                    for (int m = 1; m < (max / rate) / 60; m++)
                    {
                        var border = new Border()
                        {
                            Width = 1,
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Stretch,

                            Margin = new Thickness(MinToPixel(m), 5, 0, 0)
                        };
                        border.SetResourceReference(BackgroundProperty, "MaterialDesignBody");
                        scale.Children.Add(border);

                        for (int s = 1; s < 60; s++)
                        {
                            var border2 = new Border
                            {
                                Width = 1,
                                HorizontalAlignment = HorizontalAlignment.Left,

                                Margin = new Thickness(SecToPixel(s + m / 60), 15, 0, 0)
                            };

                            border2.SetResourceReference(BackgroundProperty, "MaterialDesignBodyLight");
                            scale.Children.Add(border2);
                        }
                    }
                }
            });
        }
        private void ClipLayerMove(ClipElement clip, int layer)
        {
            var vm = clip.GetCreateClipViewModel();
            var from = vm.Row;
            vm.Row = layer;

            Dispatcher.InvokeAsync(() =>
            {
                var togrid = (Grid)Layer.Children[layer];

                var ui = clip.GetCreateClipView();
                (ui.Parent as Grid)?.Children?.Remove(ui);

                togrid.Children.Add(ui);
            });
        }
        private async void ClipsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (e.Action == NotifyCollectionChangedAction.Add)
                {
                    var item = Scene.Datas[e.NewStartingIndex];

                    Grid grid = (Grid)Layer.Children[item.Layer];

                    grid.Children.Add(item.GetCreateClipView());
                }
                else if (e.Action == NotifyCollectionChangedAction.Remove)
                {
                    var item = e.OldItems![0];

                    if (item is ClipElement clip)
                    {
                        var ui = clip.GetCreateClipView();
                        (ui.Parent as Grid)?.Children?.Remove(ui);

                        clip.Clear();

                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                    }
                }
            });
        }

        private void ScrollLine_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            Task.Run(() =>
            {
                Scene.TimeLineHorizonOffset = ScrollLine.HorizontalOffset;
                Scene.TimeLineVerticalOffset = ScrollLine.VerticalOffset;
            });
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            _viewModel.ResetScale = AddScale;
            _viewModel.ClipLayerMoveCommand = ClipLayerMove;
            _viewModel.GetLayerMousePosition = () => Mouse.GetPosition(Layer);

            Scene.Datas.CollectionChanged += ClipsCollectionChanged;
            ScrollLine.ScrollChanged += ScrollLine_ScrollChanged1;
            ScrollLabel.ScrollChanged += ScrollLabel_ScrollChanged;

            if (_isFirstLoad)
            {
                _viewModel.TimeLineLoaded(list =>
                {
                    for (int index = 0; index < list.Count; index++)
                    {
                        var info = list[index];


                        var grid = (Grid)Layer.Children[info.Layer];

                        grid.Children.Add(info.GetCreateClipView());
                    }
                    Layer.Focus();
                });

                _viewModel.ViewLoaded = true;
                _isFirstLoad = false;
            }
        }
        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            _viewModel.ResetScale = null;
            _viewModel.ClipLayerMoveCommand = null;
            _viewModel.GetLayerMousePosition = null;

            Scene.Datas.CollectionChanged -= ClipsCollectionChanged;
            ScrollLine.ScrollChanged -= ScrollLine_ScrollChanged1;
            ScrollLabel.ScrollChanged -= ScrollLabel_ScrollChanged;
        }
    }
}
