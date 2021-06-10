using System;
using System.Linq;
using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Extensions;
using BEditor.Properties;
using BEditor.ViewModels.Timelines;

using Reactive.Bindings.Extensions;

namespace BEditor.Views.Timelines
{
    public class KeyframeView : UserControl
    {
        private readonly Grid _grid;
        private readonly TextBlock _text;
        private readonly CompositeDisposable _disposable = new();
        private readonly Animation _anm = new()
        {
            Duration = TimeSpan.FromSeconds(0.15),
            Children =
            {
                new()
                {
                    Cue = new(0),
                    Setters =
                    {
                        new Setter(OpacityProperty, 1d)
                    }
                },
                new()
                {
                    Cue = new(1),
                    Setters =
                    {
                        new Setter(OpacityProperty, 0d)
                    }
                }
            }
        };
        private Media.Frame _startpos;
        private Shape? _select;

#pragma warning disable CS8618
        public KeyframeView()
#pragma warning restore CS8618
        {
            InitializeComponent();
        }

        public KeyframeView(IKeyframeProperty property)
        {
            var viewmodel = new KeyframeViewModel(property);

            DataContext = viewmodel;
            InitializeComponent();
            _grid = this.FindControl<Grid>("grid");
            _text = this.FindControl<TextBlock>("text");

            _grid.AddHandler(PointerPressedEvent, Grid_PointerLeftPressedTunnel, RoutingStrategies.Tunnel);
            _grid.AddHandler(PointerMovedEvent, Grid_PointerMovedTunnel, RoutingStrategies.Tunnel);
            _grid.AddHandler(PointerPressedEvent, Grid_PointerRightPressedTunnel, RoutingStrategies.Tunnel);
            _grid.AddHandler(PointerReleasedEvent, Grid_PointerReleasedTunnel, RoutingStrategies.Tunnel);

            viewmodel.AddKeyFrameIcon = (frame, index) =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    index--;
                    var length = Property.GetRequiredParent<ClipElement>().Length;
                    var x = Scene.ToPixel((Media.Frame)(frame * length));
                    var icon = new Rectangle
                    {
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(x, 0, 0, 0),
                        Width = 8,
                        Height = 8,
                        RenderTransform = new RotateTransform { Angle = 45 },
                        Fill = (IBrush?)Application.Current.FindResource("SystemControlForegroundBaseMediumHighBrush")
                    };

                    Add_Handler_Icon(icon);

                    icon.ContextMenu = new ContextMenu
                    {
                        Items = new MenuItem[] { CreateMenu() }
                    };

                    _grid.Children.Insert(index, icon);
                });
            };
            viewmodel.RemoveKeyFrameIcon = (index) => Dispatcher.UIThread.InvokeAsync(() => _grid.Children.RemoveAt(index - 1));
            viewmodel.MoveKeyFrameIcon = (from, to) =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    from--;
                    to--;
                    var icon = _grid.Children[from];
                    _grid.Children.RemoveAt(from);
                    _grid.Children.Insert(to, icon);
                });
            };

            _grid.Children.Clear();

            if (Property is IKeyframeProperty<float> f)
            {
                for (var index = 1; index < f.Pairs.Count - 1; index++)
                {
                    viewmodel.AddKeyFrameIcon(f.Pairs[index].Key, index);
                }
            }
            else if (Property is IKeyframeProperty<Color> c)
            {
                for (var index = 1; index < c.Pairs.Count - 1; index++)
                {
                    viewmodel.AddKeyFrameIcon(c.Pairs[index].Key, index);
                }
            }

            var tmp = Scene.ToPixel(Property.GetParent<ClipElement>()!.Length);
            if (tmp > 0)
            {
                Width = tmp;
            }

            Scene.ObserveProperty(p => p.TimeLineZoom)
                .Subscribe(_ => ZoomChange())
                .AddTo(_disposable);

            // StoryBoardを設定
            {
                PointerEnter += async (_, _) =>
                {
                    _anm.PlaybackDirection = PlaybackDirection.Normal;
                    await _anm.RunAsync(_text);

                    _text.Opacity = 0;
                };
                PointerLeave += async (_, _) =>
                {
                    _anm.PlaybackDirection = PlaybackDirection.Reverse;
                    await _anm.RunAsync(_text);

                    _text.Opacity = 1;
                };
            }
        }

        private Scene Scene => Property.GetParent<Scene>()!;
        private IKeyframePropertyViewModel ViewModel => (IKeyframePropertyViewModel)DataContext!;
        private IKeyframeProperty Property => ViewModel.Property;

        // iconのイベントを追加
        private void Add_Handler_Icon(Shape icon)
        {
            icon.PointerPressed += Icon_PointerPressed;
            icon.PointerReleased += Icon_PointerReleased;
            icon.PointerMoved += Icon_PointerMoved;
            icon.PointerLeave += Icon_PointerLeave;
        }

        // iconのイベントを削除
        private void Remove_Handler_Icon(Shape icon)
        {
            icon.PointerPressed -= Icon_PointerPressed;
            icon.PointerReleased -= Icon_PointerReleased;
            icon.PointerMoved -= Icon_PointerMoved;
            icon.PointerLeave -= Icon_PointerLeave;
        }

        // タイムラインのスケール変更
        private void ZoomChange()
        {
            if (Property is IKeyframeProperty<float> f)
            {
                var length = Property.GetRequiredParent<ClipElement>().Length;
                for (var frame = 0; frame < f.Pairs.Count - 2; frame++)
                {
                    if (_grid.Children.Count <= frame) break;

                    if (_grid.Children[frame] is Shape icon)
                    {
                        icon.Margin = new Thickness(Scene.ToPixel((Media.Frame)(f.Pairs[frame + 1].Key * length)), 0, 0, 0);
                    }
                }

                Width = Scene.ToPixel(length);
            }
            else if (Property is IKeyframeProperty<Color> c)
            {
                var length = Property.GetRequiredParent<ClipElement>().Length;
                for (var frame = 0; frame < c.Pairs.Count - 2; frame++)
                {
                    if (_grid.Children.Count <= frame) break;

                    if (_grid.Children[frame] is Shape icon)
                    {
                        icon.Margin = new Thickness(Scene.ToPixel((Media.Frame)(c.Pairs[frame + 1].Key * length)), 0, 0, 0);
                    }
                }

                Width = Scene.ToPixel(length);
            }
        }

        // キーフレームを追加
        public void Add_Frame(object sender, RoutedEventArgs e)
        {
            ViewModel.AddKeyFrameCommand.Execute(_startpos / (float)Property.GetRequiredParent<ClipElement>().Length);
        }

        // キーフレームを削除
        private void Remove_Click(object? sender, RoutedEventArgs e)
        {
            if (Property is IKeyframeProperty<float> f)
            {
                ViewModel.RemoveKeyFrameCommand.Execute(f.Pairs[_grid.Children.IndexOf(_select) + 1].Key);
            }
            else if (Property is IKeyframeProperty<Color> c)
            {
                ViewModel.RemoveKeyFrameCommand.Execute(c.Pairs[_grid.Children.IndexOf(_select) + 1].Key);
            }
        }

        // IconのPointerPressedイベント
        // 移動開始
        private void Icon_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            _startpos = Scene.ToFrame(e.GetPosition(_grid).X);

            _select = (Shape)sender!;

            // カーソルの設定
            if (_select.Cursor == Cursors.SizeWestEast)
            {
                _grid.Cursor = Cursors.SizeWestEast;
            }

            // イベントの削除
            foreach (var icon in _grid.Children.OfType<Shape>().Where(i => i != _select))
            {
                Remove_Handler_Icon(icon);
            }
        }

        // IconのPointerReleasedイベント
        // 移動終了
        private void Icon_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            // カーソルの設定
            _grid.Cursor = Cursors.Arrow;
            if (_select is not null)
            {
                _select.Cursor = Cursors.Arrow;
            }

            // イベントの追加
            foreach (var icon in _grid.Children.OfType<Shape>().Where(i => i != _select))
            {
                Add_Handler_Icon(icon);
            }

            if (e.InitialPressMouseButton == MouseButton.Left)
            {
                Icon_PointerLeftReleased(sender, e);
            }
        }

        // IconのPointerMovedイベント
        private void Icon_PointerMoved(object? sender, PointerEventArgs e)
        {
            _select = (Shape)sender!;

            // カーソルの設定
            _select.Cursor = Cursors.SizeWestEast;

            // Timelineの一部の操作を無効化
            Scene.GetCreateTimelineViewModel().KeyframeToggle = false;
        }

        // IconのPointerLeaveイベント
        private void Icon_PointerLeave(object? sender, PointerEventArgs e)
        {
            var senderIcon = (Shape)sender!;

            // カーソルの設定
            senderIcon.Cursor = Cursors.Arrow;

            // Timelineの一部の操作を有効化
            Scene.GetCreateTimelineViewModel().KeyframeToggle = true;

            // イベントの再設定
            foreach (var icon in _grid.Children.OfType<Shape>().Where(i => i != senderIcon))
            {
                Remove_Handler_Icon(icon);
                Add_Handler_Icon(icon);
            }
        }

        // IconのPointerLeftReleasedイベント
        // 移動終了, 保存
        private void Icon_PointerLeftReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_select is not null)
            {
                // インデックス
                var idx = _grid.Children.IndexOf(_select);
                // クリップからのフレーム
                var frame = Scene.ToFrame(_select.Margin.Left) / (float)Property.GetRequiredParent<ClipElement>().Length;

                if (frame > 0 && frame < 1)
                {
                    ViewModel.MoveKeyFrameCommand.Execute((idx + 1, frame));
                }
            }
        }

        // gridのPointerMovedイベント (Tunnel)
        // iconのuiのmarginを設定
        private void Grid_PointerMovedTunnel(object? sender, PointerEventArgs e)
        {
            if (!(_select is null) && _grid.Cursor == Cursors.SizeWestEast)
            {
                // 現在のマウスの位置 (frame)
                var now = Scene.ToFrame(e.GetPosition(_grid).X);
                // クリップからのフレーム
                var a = now - _startpos + Scene.ToFrame(_select.Margin.Left);

                if (a > 0 && a < Property.GetRequiredParent<ClipElement>().Length)
                {
                    _select.Margin = new Thickness(Scene.ToPixel(a), 0, 0, 0);
                    _startpos = now;
                }
            }
        }

        // gridのPointerRightPressedイベント (Tunnel)
        private void Grid_PointerRightPressedTunnel(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint((Avalonia.VisualTree.IVisual?)sender).Properties.IsRightButtonPressed) return;

            // 右クリック -> メニュー ->「キーフレームを追加」なので
            // 現在位置を保存 (frame)
            //_nowframe = Scene.GetCreateTimeLineViewModel().ToFrame(e.GetPosition(grid).X);
            _startpos = Scene.ToFrame(e.GetPosition(_grid).X);
        }

        // gridのPointerReleasedイベント (Tunnel)
        private void Grid_PointerReleasedTunnel(object? sender, PointerReleasedEventArgs e)
        {
            // カーソルの設定
            _grid.Cursor = Cursors.Arrow;
            if (_select is null) return;

            if (_select.Cursor == Cursors.SizeWestEast)
            {
                _grid.Cursor = Cursors.SizeWestEast;
            }
        }

        // gridのPointerLeftPressedイベント (Tunnel)
        private void Grid_PointerLeftPressedTunnel(object? sender, PointerPressedEventArgs e)
        {
            if (_select is null || !e.GetCurrentPoint((Avalonia.VisualTree.IVisual?)sender).Properties.IsLeftButtonPressed) return;

            if (_select.Cursor == Cursors.SizeWestEast)
            {
                // 現在位置を保存
                _startpos = Scene.ToFrame(e.GetPosition(_grid).X);
            }
        }

        // gridのPointerLeaveイベント
        public void Grid_PointerLeave(object sender, PointerEventArgs e)
        {
            // カーソルの設定
            _grid.Cursor = Cursors.Arrow;
            if (_select is null) return;

            if (_select.Cursor == Cursors.SizeWestEast)
            {
                _grid.Cursor = Cursors.SizeWestEast;
            }
        }

        // Iconのメニューを作成
        private MenuItem CreateMenu()
        {
            var removeMenu = new MenuItem
            {
                Icon = new PathIcon
                {
                    Data = (Geometry)Application.Current.FindResource("Delete20Regular")!,
                    Margin = new Thickness(5, 0, 5, 0)
                },
                Header = Strings.Remove
            };

            removeMenu.Click += Remove_Click;

            return removeMenu;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}