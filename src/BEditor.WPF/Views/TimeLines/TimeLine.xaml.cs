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

using Microsoft.Xaml.Behaviors;

using Resource = BEditor.Properties.Resources;

namespace BEditor.Views.TimeLines
{
    /// <summary>
    /// TimeLine.xaml の相互作用ロジック
    /// </summary>
    public partial class TimeLine : UserControl
    {
        private readonly Scene _scene;
        private readonly TimeLineViewModel _viewModel;

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

        public TimeLine(Scene scene)
        {
            ContextMenu CreateMenu(int layer)
            {
                ContextMenu contextMenu = new ContextMenu();

                #region 削除

                MenuItem Delete = new MenuItem();

                var deletemenu = new VirtualizingStackPanel() { Orientation = Orientation.Horizontal };
                deletemenu.Children.Add(new PackIcon() { Kind = PackIconKind.Delete, Margin = new Thickness(5, 0, 5, 0) });
                deletemenu.Children.Add(new TextBlock() { Text = Resource.Remove, Margin = new Thickness(20, 0, 5, 0) });
                Delete.Header = deletemenu;

                contextMenu.Items.Add(Delete);

                Delete.Click += (_, _) =>
                {
                    scene.RemoveLayer(layer).Execute();
                };

                #endregion

                return contextMenu;
            }

            _scene = scene;
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
                grid.PreviewDragOver += (s, e) => _viewModel.LayerDragOverCommand.Execute((s,e));

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
                            _scene.HideLayer.Remove(l);
                        }
                        else
                        {
                            _scene.HideLayer.Add(l);
                        }

                        _scene.Parent!.PreviewUpdate();
                    };

                    if (_scene.HideLayer.Exists(x => x == l))
                    {
                        toggle.IsChecked = true;
                    }

                    LayerLabel.Children.Add(layer_row);
                }

                #endregion
            }

            ScrollLabel.ScrollToVerticalOffset(_scene.TimeLineVerticalOffset);
            ScrollLine.ScrollToVerticalOffset(_scene.TimeLineVerticalOffset);
            ScrollLine.ScrollToHorizontalOffset(_scene.TimeLineHorizonOffset);

            var linetrigger = Interaction.GetTriggers(ScrollLine);
            linetrigger.Add(CommandTool.CreateEvent("ScrollChanged", _viewModel.ScrollLineCommand));

            var labeltrigger = Interaction.GetTriggers(ScrollLabel);
            labeltrigger.Add(CommandTool.CreateEvent("ScrollChanged", _viewModel.ScrollLabelCommand));

            _viewModel.ResetScale = (zoom, max, rate) => AddScale(zoom, max, rate);
            _viewModel.ClipLayerMoveCommand = (data, layer) =>
            {
                var vm = data.GetCreateClipViewModel();
                var from = vm.Row;
                vm.Row = layer;

                App.Current.Dispatcher.InvokeAsync(() =>
                {
                    Grid togrid = (Grid)Layer.Children[layer];

                    var ui = data.GetCreateClipView();
                    (ui.Parent as Grid)?.Children?.Remove(ui);

                    togrid.Children.Add(ui);
                });
            };
            _viewModel.GetLayerMousePosition = () => Mouse.GetPosition(Layer);

            _viewModel.ViewLoaded = true;
            _viewModel.TimeLineLoaded(list =>
            {
                for (int index = 0; index < list.Count; index++)
                {
                    var info = list[index];


                    Grid grid = (Grid)Layer.Children[info.Layer];

                    grid.Children.Add(info.GetCreateClipView());
                }
                Layer.Focus();
            });

            _scene.Datas.CollectionChanged += async (s, e) =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (e.Action == NotifyCollectionChangedAction.Add)
                    {
                        var item = _scene.Datas[e.NewStartingIndex];

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

                            clip.GetValue(ViewBuilder.ClipViewModelProperty)?.Dispose();

                            clip.Clear();

                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                            GC.Collect();
                        }
                    }
                });
            };
        }


        #region Scrollbarの移動量を変更

        private void ScrollLine_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ScrollViewer scrollviewer = (ScrollViewer)sender;

            if (Keyboard.IsKeyDown(Key.LeftCtrl))
            {
                if (!(_scene.TimeLineZoom > 200 || _scene.TimeLineZoom < 1))
                {
                    var offset = scrollviewer.HorizontalOffset;
                    var frame = _viewModel.ToFrame(offset);
                    _scene.TimeLineZoom += (e.Delta / 120) * 5;

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

        #endregion

        /// <summary>
        /// 目盛りを追加するメソッド
        /// </summary>
        /// <param name="zoom">拡大率 1 - 200</param>
        /// <param name="max">最大フレーム</param>
        /// <param name="rate">フレームレート</param>
        private void AddScale(float zoom, int max, int rate)
        {
            App.Current.Dispatcher.InvokeAsync(() =>
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
                        Border border = new Border
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
                                Border border2 = new Border
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
                                Border border2 = new Border
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
                                Border border2 = new Border
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
                        Border border = new Border()
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
                            Border border2 = new Border
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

        private void ScrollLine_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            Task.Run(() =>
            {
                _scene.TimeLineHorizonOffset = ScrollLine.HorizontalOffset;
                _scene.TimeLineVerticalOffset = ScrollLine.VerticalOffset;
            });
        }
    }
}
