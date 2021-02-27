using System;
using System.ComponentModel;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.ViewModels.TimeLines;
using BEditor.WPF.Controls;

using MaterialDesignThemes.Wpf;

using Reactive.Bindings.Extensions;

using Resource = BEditor.Properties.Resources;

namespace BEditor.Views.TimeLines
{
    /// <summary>
    /// ColorAnimation.xaml の相互作用ロジック
    /// </summary>
    public sealed partial class ColorAnimation : UserControl, ICustomTreeViewItem, IDisposable
    {
        private ColorAnimationProperty _property;
        private bool _addToggle;
        private int _startPos;
        private PackIcon? _select;
        private Media.Frame _nowframe;
        private readonly Storyboard _getStoryboard = new Storyboard();
        private readonly Storyboard _lostStoryboard = new Storyboard();
        private readonly DoubleAnimation _getAnm = new DoubleAnimation() { Duration = TimeSpan.FromSeconds(0.15), To = 0 };
        private readonly DoubleAnimation _lostAnm = new DoubleAnimation() { Duration = TimeSpan.FromSeconds(0.15), To = 1 };
        private readonly CompositeDisposable _disposable = new();

        public ColorAnimation(ColorAnimationProperty property)
        {
            var viewModel = new ColorAnimationViewModel(property);
            DataContext = viewModel;
            InitializeComponent();
            _property = property;

            viewModel.AddKeyFrameIcon = (frame, index) =>
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

                    IconAddEventHandler(icon);

                    icon.ContextMenu = new ContextMenu();
                    icon.ContextMenu.Items.Add(CreateMenu());

                    grid.Children.Insert(index, icon);

                });
            };
            viewModel.RemoveKeyFrameIcon = (index) => Dispatcher.InvokeAsync(() => grid.Children.RemoveAt(index));
            viewModel.MoveKeyFrameIcon = (from, to) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    var icon = grid.Children[from];
                    grid.Children.RemoveAt(from);
                    grid.Children.Insert(to, icon);
                });
            };

            grid.Children.Clear();


            for (int index = 0; index < property.Frame.Count; index++)
            {
                viewModel.AddKeyFrameIcon(property.Frame[index], index);
            }

            var tmp = Scene.GetCreateTimeLineViewModel().ToPixel(property.GetParent2()!.Length);
            if (tmp > 0)
            {
                Width = tmp;
            }

            Scene.ObserveProperty(p => p.TimeLineZoom)
                .Subscribe(_ => ZoomChange())
                .AddTo(_disposable);

            //StoryBoard
            Storyboard.SetTarget(_getAnm, text);
            Storyboard.SetTargetProperty(_getAnm, new PropertyPath("(Opacity)"));

            Storyboard.SetTarget(_lostAnm, text);
            Storyboard.SetTargetProperty(_lostAnm, new PropertyPath("(Opacity)"));

            _getStoryboard.Children.Add(_getAnm);
            _lostStoryboard.Children.Add(_lostAnm);

            MouseEnter += (_, _) => _getStoryboard.Begin();
            MouseLeave += (_, _) => _lostStoryboard.Begin();
        }
        ~ColorAnimation()
        {
            Dispose();
        }


        private Scene Scene => _property.GetParent3()!;
        private ColorAnimationViewModel ViewModel => (ColorAnimationViewModel)DataContext;
        public double LogicHeight => Settings.Default.ClipHeight + 1;


        private void IconAddEventHandler(PackIcon icon)
        {
            icon.MouseDown += IconMouseDown;
            icon.MouseUp += IconMouseup;
            icon.MouseMove += IconMouseMove;
            icon.MouseLeave += IconMouseLeave;
            icon.MouseLeftButtonUp += IconMouseLeftUp;
        }
        private void IconRemoveEventHandler(PackIcon icon)
        {
            icon.MouseDown -= IconMouseDown;
            icon.MouseUp -= IconMouseup;
            icon.MouseMove -= IconMouseMove;
            icon.MouseLeave -= IconMouseLeave;
            icon.MouseLeftButtonUp -= IconMouseLeftUp;
        }

        private void ZoomChange()
        {
            for (int frame = 0; frame < _property.Frame.Count; frame++)
            {
                if (grid.Children[frame] is PackIcon icon)
                {
                    icon.Margin = new Thickness(Scene.GetCreateTimeLineViewModel().ToPixel(_property.Frame[frame]), 0, 0, 0);
                }
            }

            Width = Scene.GetCreateTimeLineViewModel().ToPixel(_property.GetParent2()!.Length);
        }

        private void Add_Pos(object sender, RoutedEventArgs e)
        {
            ViewModel.AddKeyFrameCommand.Execute(_nowframe);
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.RemoveKeyFrameCommand.Execute(_property.Frame[grid.Children.IndexOf(_select)]);
        }

        private void Mouse_Move(object sender, MouseEventArgs e)
        {
            if (_addToggle)
            {
                _nowframe = Scene.GetCreateTimeLineViewModel().ToFrame(e.GetPosition(grid).X);

                _addToggle = false;
            }
            else if (_select is null)
            {
                return;
            }
            else if (grid.Cursor == Cursors.SizeWE)
            {
                var now = Scene.GetCreateTimeLineViewModel().ToFrame(e.GetPosition(grid).X);
                var a = now - _startPos + Scene.GetCreateTimeLineViewModel().ToFrame(_select.Margin.Left);//相対

                _select.Margin = new Thickness(Scene.GetCreateTimeLineViewModel().ToPixel(a), 0, 0, 0);

                _startPos = now;
            }
        }

        private void Mouse_RightDown(object sender, MouseButtonEventArgs e)
        {
            _addToggle = true;
        }

        private void IconMouseDown(object sender, MouseButtonEventArgs e)
        {
            _startPos = Scene.GetCreateTimeLineViewModel().ToFrame(e.GetPosition(grid).X);

            _select = (PackIcon)sender;
            if (_select.Cursor == Cursors.SizeWE)
            {
                grid.Cursor = Cursors.SizeWE;
            }

            for (int i = 0; i < grid.Children.Count; i++)
            {
                var icon = (PackIcon)grid.Children[i];

                if (icon != _select)
                {
                    IconRemoveEventHandler(icon);
                }
            }
        }

        private void IconMouseup(object sender, MouseButtonEventArgs e)
        {
            grid.Cursor = Cursors.Arrow;
            if (_select is not null)
            {
                _select.Cursor = Cursors.Arrow;
            }

            for (int i = 0; i < grid.Children.Count; i++)
            {
                var icon = (PackIcon)grid.Children[i];

                if (icon != (sender as PackIcon))
                {
                    IconAddEventHandler(icon);
                }
            }
        }

        private void IconMouseMove(object sender, MouseEventArgs e)
        {
            _select = (PackIcon)sender;

            _select.Cursor = Cursors.SizeWE;
            Scene.GetCreateTimeLineViewModel().KeyframeToggle = false;
        }

        private void IconMouseLeave(object sender, MouseEventArgs e)
        {
            var senderIcon = (PackIcon)sender;
            senderIcon.Cursor = Cursors.Arrow;
            Scene.GetCreateTimeLineViewModel().KeyframeToggle = true;


            for (int i = 0; i < grid.Children.Count; i++)
            {
                var icon = (PackIcon)grid.Children[i];

                if (icon != senderIcon)
                {
                    IconRemoveEventHandler(icon);
                    IconAddEventHandler(icon);
                }
            }
        }

        private void IconMouseLeftUp(object sender, MouseButtonEventArgs e)
        {
            if (_select is not null)
            {
                ViewModel.MoveKeyFrameCommand.Execute((grid.Children.IndexOf(_select), Scene.GetCreateTimeLineViewModel().ToFrame(_select.Margin.Left)));
            }
        }

        private void Mouseup(object sender, MouseButtonEventArgs e)
        {
            grid.Cursor = Cursors.Arrow;
            if (_select is null) return;

            if (_select.Cursor == Cursors.SizeWE)
            {
                grid.Cursor = Cursors.SizeWE;
            }
        }

        private void Mouse_Down(object sender, MouseButtonEventArgs e)
        {
            if (_select is null) return;

            if (_select.Cursor == Cursors.SizeWE)
            {
                _startPos = Scene.GetCreateTimeLineViewModel().ToFrame(e.GetPosition(grid).X);
            }
        }

        private void Mouse_Leave(object sender, MouseEventArgs e)
        {
            grid.Cursor = Cursors.Arrow;
            if (_select is null) return;

            if (_select.Cursor == Cursors.SizeWE)
            {
                grid.Cursor = Cursors.SizeWE;
            }
        }

        private MenuItem CreateMenu()
        {
            var removeMenu = new MenuItem();

            //削除項目の設定
            var menu = new VirtualizingStackPanel() { Orientation = Orientation.Horizontal };
            menu.Children.Add(new PackIcon() { Kind = PackIconKind.Delete, Margin = new Thickness(5, 0, 5, 0) });
            menu.Children.Add(new TextBlock() { Text = Resource.Remove, Margin = new Thickness(20, 0, 5, 0) });
            removeMenu.Header = menu;

            removeMenu.Click += Delete_Click;

            return removeMenu;
        }

        public void Dispose()
        {
            _disposable.Dispose();
            _disposable.Clear();
            DataContext = null;
            _property = null!;

            GC.SuppressFinalize(this);
        }
    }
}
