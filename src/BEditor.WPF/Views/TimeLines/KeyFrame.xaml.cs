using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Models;
using BEditor.Properties;
using BEditor.ViewModels.TimeLines;
using BEditor.WPF.Controls;

using MaterialDesignThemes.Wpf;

using Reactive.Bindings.Extensions;

namespace BEditor.Views.TimeLines
{
    /// <summary>
    /// KeyFrame.xaml の相互作用ロジック
    /// </summary>
    public sealed partial class KeyFrame : UserControl, ICustomTreeViewItem, IDisposable
    {
        private Media.Frame _startpos;
        private PackIcon? _select;
        private readonly Storyboard _getStoryboard = new();
        private readonly Storyboard _lostStoryboard = new();
        private readonly CompositeDisposable _disposable = new();


        public KeyFrame(IKeyframeProperty property)
        {
            IKeyframePropertyViewModel? viewmodel = new KeyFrameViewModel(property);

            DataContext = viewmodel;
            InitializeComponent();

            viewmodel.AddKeyFrameIcon = (frame, index) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    var x = Scene.GetCreateTimeLineViewModel().ToPixel(frame);
                    var icon = new PackIcon()
                    {
                        Kind = PackIconKind.RhombusMedium,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(x, 0, 0, 0),
                        Background = new SolidColorBrush(Colors.Transparent)
                    };

                    icon.SetResourceReference(ForegroundProperty, "MaterialDesignCardBackground");

                    Add_Handler_Icon(icon);

                    icon.ContextMenu = new ContextMenu();
                    icon.ContextMenu.Items.Add(CreateMenu());

                    grid.Children.Insert(index, icon);
                });
            };
            viewmodel.RemoveKeyFrameIcon = (index) => Dispatcher.InvokeAsync(() => grid.Children.RemoveAt(index));
            viewmodel.MoveKeyFrameIcon = (from, to) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    var icon = grid.Children[from];
                    grid.Children.RemoveAt(from);
                    grid.Children.Insert(to, icon);
                });
            };

            grid.Children.Clear();

            for (int index = 0; index < Property.Frames.Count; index++)
            {
                viewmodel.AddKeyFrameIcon(Property.Frames[index], index);
            }

            var tmp = Scene.GetCreateTimeLineViewModel().ToPixel(Property.GetParent<ClipElement>()!.Length);
            if (tmp > 0)
            {
                Width = tmp;
            }

            Scene.ObserveProperty(p => p.TimeLineZoom)
                .Subscribe(_ => ZoomChange())
                .AddTo(_disposable);

            // StoryBoardを設定
            {
                var getAnm = new DoubleAnimation() { Duration = TimeSpan.FromSeconds(0.15), To = 0 };
                var lostAnm = new DoubleAnimation() { Duration = TimeSpan.FromSeconds(0.15), To = 1 };

                Storyboard.SetTarget(getAnm, text);
                Storyboard.SetTargetProperty(getAnm, new PropertyPath("(Opacity)"));

                Storyboard.SetTarget(lostAnm, text);
                Storyboard.SetTargetProperty(lostAnm, new PropertyPath("(Opacity)"));

                _getStoryboard.Children.Add(getAnm);
                _lostStoryboard.Children.Add(lostAnm);

                MouseEnter += (_, _) => _getStoryboard.Begin();
                MouseLeave += (_, _) => _lostStoryboard.Begin();
            }
        }
        ~KeyFrame()
        {
            Dispose();
        }


        private Scene Scene => Property.GetParent<Scene>()!;
        private IKeyframePropertyViewModel ViewModel => (IKeyframePropertyViewModel)DataContext;
        private IKeyframeProperty Property => ViewModel.Property;
        public double LogicHeight => Setting.ClipHeight + 1;

        // iconのイベントを追加
        private void Add_Handler_Icon(PackIcon icon)
        {
            icon.MouseDown += Icon_Mouse_Down;
            icon.MouseUp += Icon_Mouse_Up;
            icon.MouseMove += Icon_Mouse_Move;
            icon.MouseLeave += Icon_Mouse_Leave;
            icon.MouseLeftButtonUp += Icon_Mouse_Left_Up;
        }

        // iconのイベントを削除
        private void Remove_Handler_Icon(PackIcon icon)
        {
            icon.MouseDown -= Icon_Mouse_Down;
            icon.MouseUp -= Icon_Mouse_Up;
            icon.MouseMove -= Icon_Mouse_Move;
            icon.MouseLeave -= Icon_Mouse_Leave;
            icon.MouseLeftButtonUp -= Icon_Mouse_Left_Up;
        }

        // タイムラインのスケール変更
        private void ZoomChange()
        {
            for (int frame = 0; frame < Property.Frames.Count; frame++)
            {
                if (grid.Children[frame] is PackIcon icon)
                {
                    icon.Margin = new Thickness(Scene.GetCreateTimeLineViewModel().ToPixel(Property.Frames[frame]), 0, 0, 0);
                }
            }

            Width = Scene.GetCreateTimeLineViewModel().ToPixel(Property.GetParent<ClipElement>()!.Length);
        }

        // キーフレームを追加
        private void Add_Frame(object sender, RoutedEventArgs e)
        {
            //ViewModel.AddKeyFrameCommand.Execute(_nowframe);
            ViewModel.AddKeyFrameCommand.Execute(_startpos);
        }

        // キーフレームを削除
        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.RemoveKeyFrameCommand.Execute(Property.Frames[grid.Children.IndexOf(_select)]);
        }

        // IconのMouseDownイベント
        // 移動開始
        private void Icon_Mouse_Down(object sender, MouseButtonEventArgs e)
        {
            _startpos = Scene.GetCreateTimeLineViewModel().ToFrame(e.GetPosition(grid).X);

            _select = (PackIcon)sender;

            // カーソルの設定
            if (_select.Cursor == Cursors.SizeWE)
            {
                grid.Cursor = Cursors.SizeWE;
            }

            // イベントの削除
            foreach (var icon in grid.Children.OfType<PackIcon>().Where(i => i != _select))
            {
                Remove_Handler_Icon(icon);
            }
        }

        // IconのMouseUpイベント
        // 移動終了
        private void Icon_Mouse_Up(object sender, MouseButtonEventArgs e)
        {
            // カーソルの設定
            grid.Cursor = Cursors.Arrow;
            if (_select is not null)
            {
                _select.Cursor = Cursors.Arrow;
            }

            // イベントの追加
            foreach (var icon in grid.Children.OfType<PackIcon>().Where(i => i != _select))
            {
                Add_Handler_Icon(icon);
            }
        }

        // IconのMouseMoveイベント
        private void Icon_Mouse_Move(object sender, MouseEventArgs e)
        {
            _select = (PackIcon)sender;

            // カーソルの設定
            _select.Cursor = Cursors.SizeWE;

            // Timelineの一部の操作を無効化
            Scene.GetCreateTimeLineViewModel().KeyframeToggle = false;
        }

        // IconのMouseLeaveイベント
        private void Icon_Mouse_Leave(object sender, MouseEventArgs e)
        {
            var senderIcon = (PackIcon)sender;

            // カーソルの設定
            senderIcon.Cursor = Cursors.Arrow;

            // Timelineの一部の操作を有効化
            Scene.GetCreateTimeLineViewModel().KeyframeToggle = true;

            // イベントの再設定
            foreach (var icon in grid.Children.OfType<PackIcon>().Where(i => i != senderIcon))
            {
                Remove_Handler_Icon(icon);
                Add_Handler_Icon(icon);
            }
        }

        // IconのMouseLeftButtonUpイベント
        // 移動終了, 保存
        private void Icon_Mouse_Left_Up(object sender, MouseButtonEventArgs e)
        {
            if (_select is not null)
            {
                // インデックス
                var idx = grid.Children.IndexOf(_select);
                // クリップからのフレーム
                var frame = Scene.GetCreateTimeLineViewModel().ToFrame(_select.Margin.Left);

                ViewModel.MoveKeyFrameCommand.Execute((idx, frame));
            }
        }

        // gridのPreviewMouseMoveイベント
        // iconのuiのmarginを設定
        private void Grid_Mouse_Move(object sender, MouseEventArgs e)
        {
            if (_select is null) return;
            else if (grid.Cursor == Cursors.SizeWE)
            {
                // 現在のマウスの位置 (frame)
                var now = Scene.GetCreateTimeLineViewModel().ToFrame(e.GetPosition(grid).X);
                // クリップからのフレーム
                var a = now - _startpos + Scene.GetCreateTimeLineViewModel().ToFrame(_select.Margin.Left);

                _select.Margin = new Thickness(Scene.GetCreateTimeLineViewModel().ToPixel(a), 0, 0, 0);

                _startpos = now;
            }
        }

        // gridのPreviewMouseRightButtonDownイベント
        private void Grid_Mouse_Right_Down(object sender, MouseButtonEventArgs e)
        {
            // 右クリック -> メニュー ->「キーフレームを追加」なので
            // 現在位置を保存 (frame)
            //_nowframe = Scene.GetCreateTimeLineViewModel().ToFrame(e.GetPosition(grid).X);
            _startpos = Scene.GetCreateTimeLineViewModel().ToFrame(e.GetPosition(grid).X);
        }

        // gridのPreviewMouseUpイベント
        private void Grid_Mouse_Up(object sender, MouseButtonEventArgs e)
        {
            // カーソルの設定
            grid.Cursor = Cursors.Arrow;
            if (_select is null) return;

            if (_select.Cursor == Cursors.SizeWE)
            {
                grid.Cursor = Cursors.SizeWE;
            }
        }

        // gridのPreviewMouseLeftButtonDownイベント
        private void Grid_Mouse_Down(object sender, MouseButtonEventArgs e)
        {
            if (_select is null) return;

            if (_select.Cursor == Cursors.SizeWE)
            {
                // 現在位置を保存
                _startpos = Scene.GetCreateTimeLineViewModel().ToFrame(e.GetPosition(grid).X);
            }
        }

        // gridのMouseLeaveイベント
        private void Grid_Mouse_Leave(object sender, MouseEventArgs e)
        {
            // カーソルの設定
            grid.Cursor = Cursors.Arrow;
            if (_select is null) return;

            if (_select.Cursor == Cursors.SizeWE)
            {
                grid.Cursor = Cursors.SizeWE;
            }
        }

        // Iconのメニューを作成
        private MenuItem CreateMenu()
        {
            var removeMenu = new MenuItem();

            //削除項目の設定
            var menu = new VirtualizingStackPanel()
            {
                Orientation = Orientation.Horizontal
            };
            menu.Children.Add(new PackIcon()
            {
                Kind = PackIconKind.Delete,
                Margin = new Thickness(5, 0, 5, 0)
            });
            menu.Children.Add(new TextBlock()
            {
                Text = Strings.Remove,
                Margin = new Thickness(20, 0, 5, 0)
            });
            removeMenu.Header = menu;

            removeMenu.Click += Remove_Click;

            return removeMenu;
        }

        public void Dispose()
        {
            _disposable.Dispose();
            _disposable.Clear();
            DataContext = null;

            GC.SuppressFinalize(this);
        }
    }
}