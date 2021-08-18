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
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Extensions;
using BEditor.Properties;
using BEditor.ViewModels.Timelines;

using Reactive.Bindings;
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
        private Size _recentSize;

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

            viewmodel.AddKeyFrameIcon = pos =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var index = Property.IndexOf(pos);
                    index--;
                    var length = Property.GetRequiredParent<ClipElement>().Length;
                    var x = Scene.ToPixel((Media.Frame)(pos.GetAbsolutePosition(length)));
                    var icon = new Rectangle
                    {
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(x, 0, 0, 0),
                        Width = 8,
                        Height = 8,
                        RenderTransform = new RotateTransform { Angle = 45 },
                        Fill = (IBrush?)Application.Current.FindResource("TextControlForeground"),
                        Tag = pos,
                    };

                    Add_Handler_Icon(icon);

                    //icon.ContextMenu = new ContextMenu
                    //{
                    //    Items = new MenuItem[] { CreateMenu() }
                    //};

                    icon.ContextMenu = CreateContextMenu(pos);

                    _grid.Children.Insert(index, icon);
                });
            };
            viewmodel.RemoveKeyFrameIcon = (pos) => Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var item in _grid.Children)
                {
                    if (item is Shape shape && shape.Tag is PositionInfo pi && pi == pos)
                    {
                        _grid.Children.Remove(item);
                        break;
                    }
                }
            });
            viewmodel.MoveKeyFrameIcon = (from, to) =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var tag = Property.Enumerate().ElementAt(to);
                    from--;
                    to--;
                    var icon = (Shape)_grid.Children[from];
                    icon.Tag = tag;

                    _grid.Children.RemoveAt(from);
                    _grid.Children.Insert(to, icon);
                });
            };

            _grid.Children.Clear();

            if (Property is IKeyframeProperty<float> f)
            {
                for (var index = 1; index < f.Pairs.Count - 1; index++)
                {
                    viewmodel.AddKeyFrameIcon(f.Pairs[index].Position);
                }
            }
            else if (Property is IKeyframeProperty<Drawing.Color> c)
            {
                for (var index = 1; index < c.Pairs.Count - 1; index++)
                {
                    viewmodel.AddKeyFrameIcon(c.Pairs[index].Position);
                }
            }

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
        private KeyframeViewModel ViewModel => (KeyframeViewModel)DataContext!;
        private IKeyframeProperty Property => ViewModel.Property;

        // サイズ変更
        protected override Size MeasureOverride(Size availableSize)
        {
            if (_recentSize != availableSize)
            {
                if (Property is IKeyframeProperty<float> f)
                {
                    var length = Scene.ToFrame(availableSize.Width);
                    for (var frame = 0; frame < f.Pairs.Count - 2; frame++)
                    {
                        if (_grid.Children.Count <= frame) break;

                        if (_grid.Children[frame] is Shape icon)
                        {
                            icon.Margin = new Thickness(Scene.ToPixel((Media.Frame)f.Pairs[frame + 1].Position.GetAbsolutePosition(length)), 0, 0, 0);
                        }
                    }
                }
                else if (Property is IKeyframeProperty<Drawing.Color> c)
                {
                    var length = Scene.ToFrame(availableSize.Width);
                    for (var frame = 0; frame < c.Pairs.Count - 2; frame++)
                    {
                        if (_grid.Children.Count <= frame) break;

                        if (_grid.Children[frame] is Shape icon)
                        {
                            icon.Margin = new Thickness(Scene.ToPixel((Media.Frame)c.Pairs[frame + 1].Position.GetAbsolutePosition(length)), 0, 0, 0);
                        }
                    }
                }
                _recentSize = availableSize;
            }

            return base.MeasureOverride(availableSize);
        }

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

        // キーフレームを追加
        public void Add_Frame(object sender, RoutedEventArgs e)
        {
            ViewModel.AddKeyFrameCommand.Execute(new(_startpos / (float)Property.GetRequiredParent<ClipElement>().Length, PositionType.Percentage));
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

                if (now > 0 && now < Property.GetRequiredParent<ClipElement>().Length)
                {
                    _select.Margin = new Thickness(Scene.ToPixel(now), 0, 0, 0);
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
        private ContextMenu CreateContextMenu(PositionInfo position)
        {
            var context = new ContextMenu();

            var removeMenu = new MenuItem
            {
                Icon = new FluentAvalonia.UI.Controls.SymbolIcon
                {
                    Symbol = FluentAvalonia.UI.Controls.Symbol.Delete,
                    FontSize = 20,
                },
                Header = Strings.Remove
            };

            removeMenu.Click += Remove_Click;

            var saveAsFrameNumberMenu = new MenuItem
            {
                Header = Strings.SavePositionAsFrameNumber
            };

            saveAsFrameNumberMenu.Click += SaveAsFrameNumber_Click;

            var saveAsPercentageMenu = new MenuItem
            {
                Header = Strings.SavePositionAsPercentage
            };

            saveAsPercentageMenu.Click += SaveAsPercentage_Click;

            var icon = new FluentAvalonia.UI.Controls.PathIcon
            {
                Data = StreamGeometry.Parse("M0,2a2,2 0 1,0 4,0a2,2 0 1,0 -4,0"),
                UseLayoutRounding = false,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            if (position.Type == PositionType.Percentage)
            {
                saveAsPercentageMenu.Icon = icon;
            }
            else
            {
                saveAsFrameNumberMenu.Icon = icon;
            }

            saveAsPercentageMenu.Tag = saveAsFrameNumberMenu;
            saveAsFrameNumberMenu.Tag = saveAsPercentageMenu;

            context.Items = new object[]
            {
                removeMenu,
                saveAsFrameNumberMenu,
                saveAsPercentageMenu
            };

            return context;
        }

        // PositionTypeをPercentageに変更
        private void SaveAsPercentage_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menu && menu.Tag is MenuItem menu1)
            {
                var shape = menu.FindLogicalAncestorOfType<Shape>();
                if (shape.Tag is PositionInfo pos && pos.Type != PositionType.Percentage)
                {
                    var index = Property.IndexOf(pos);
                    pos = pos.WithType(PositionType.Percentage, Property.GetRequiredParent<ClipElement>().Length);
                    Property.UpdatePositionInfo(index, pos).Execute();
                    shape.Tag = pos;

                    // アイコン変更
                    var icon = menu1.Icon;
                    menu1.Icon = null!;
                    menu.Icon = icon;
                }
            }
        }

        // PositionTypeをAbsに変更
        private void SaveAsFrameNumber_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menu && menu.Tag is MenuItem menu1)
            {
                var shape = menu.FindLogicalAncestorOfType<Shape>();
                if (shape.Tag is PositionInfo pos && pos.Type != PositionType.Absolute)
                {
                    var index = Property.IndexOf(pos);
                    pos = pos.WithType(PositionType.Absolute, Property.GetRequiredParent<ClipElement>().Length);
                    Property.UpdatePositionInfo(index, pos).Execute();
                    shape.Tag = pos;

                    // アイコン変更
                    var icon = menu1.Icon;
                    menu1.Icon = null!;
                    menu.Icon = icon;
                }
            }
        }

        // キーフレームを削除
        private void Remove_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menu)
            {
                var shape = menu.FindLogicalAncestorOfType<Shape>();
                if (shape.Tag is PositionInfo pos)
                {
                    ViewModel.RemoveKeyFrameCommand.Execute(pos);
                }
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}