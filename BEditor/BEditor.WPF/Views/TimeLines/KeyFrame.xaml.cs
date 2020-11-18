using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

using BEditor.Models.Settings;
using BEditor.ViewModels.TimeLines;
using BEditor.Views.CustomControl;

using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;

using MaterialDesignThemes.Wpf;

using Resource = BEditor.Core.Properties.Resources;
using BEditor.Core.Extensions;

namespace BEditor.Views.TimeLines
{
    /// <summary>
    /// KeyFrame.xaml の相互作用ロジック
    /// </summary>
    public partial class KeyFrame : UserControl, ICustomTreeViewItem
    {
        public static readonly DependencyProperty AddCommandProperty = DependencyProperty.Register("AddCommand", typeof(ICommand), typeof(KeyFrame));
        public static readonly DependencyProperty RemoveCommandProperty = DependencyProperty.Register("RemoveCommand", typeof(ICommand), typeof(KeyFrame));
        public static readonly DependencyProperty MoveCommandProperty = DependencyProperty.Register("MoveCommand", typeof(ICommand), typeof(KeyFrame));

        private readonly EaseProperty EaseList;
        private readonly Scene Scene;

        public ICommand AddCommand
        {
            get => (ICommand)GetValue(AddCommandProperty);
            set => SetValue(AddCommandProperty, value);
        }
        public ICommand RemoveCommand
        {
            get => (ICommand)GetValue(RemoveCommandProperty);
            set => SetValue(RemoveCommandProperty, value);
        }
        public ICommand MoveCommand
        {
            get => (ICommand)GetValue(MoveCommandProperty);
            set => SetValue(MoveCommandProperty, value);
        }

        #region ICustomTreeViewItem
        public double LogicHeight => Setting.ClipHeight + 1;
        #endregion

        #region コンストラクタ
        public KeyFrame(Scene scene, EaseProperty easingList)
        {
            InitializeComponent();
            EaseList = easingList;
            Scene = scene;
            var viewmodel = new KeyFrameViewModel(easingList);
            DataContext = viewmodel;

            viewmodel.AddKeyFrameIcon = (frame, index) =>
            {
                App.Current?.Dispatcher?.Invoke(() =>
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

                    #region イベント
                    icon.MouseDown += IconMouseDown;
                    icon.MouseUp += IconMouseup;
                    icon.MouseMove += IconMouseMove;
                    icon.MouseLeave += IconMouseLeave;
                    icon.MouseLeftButtonUp += IconMouseLeftUp;
                    #endregion

                    icon.ContextMenu = new ContextMenu();
                    icon.ContextMenu.Items.Add(CreateMenu());

                    grid.Children.Insert(index, icon);
                });
            };
            viewmodel.DeleteKeyFrameIcon = (index) => App.Current?.Dispatcher?.Invoke(() => grid.Children.RemoveAt(index));
            viewmodel.MoveKeyFrameIcon = (from, to) =>
            {
                App.Current?.Dispatcher?.Invoke(() =>
                {
                    var icon = grid.Children[from];
                    grid.Children.RemoveAt(from);
                    grid.Children.Insert(to, icon);
                });
            };

            grid.Children.Clear();


            SetBinding(AddCommandProperty, new Binding("AddKeyFrameCommand") { Mode = BindingMode.OneTime });
            SetBinding(RemoveCommandProperty, new Binding("RemoveKeyFrameCommand") { Mode = BindingMode.OneTime });
            SetBinding(MoveCommandProperty, new Binding("MoveKeyFrameCommand") { Mode = BindingMode.OneTime });


            for (int index = 0; index < EaseList.Time.Count; index++)
            {
                viewmodel.AddKeyFrameIcon(EaseList.Time[index], index);
            }

            var tmp = Scene.GetCreateTimeLineViewModel().ToPixel(EaseList.GetParent2().Length);
            if (tmp > 0)
            {
                Width = tmp;
            }

            Scene.PropertyChanged += ZoomChange;

            //StoryBoard
            Storyboard.SetTarget(GetAnm, text);
            Storyboard.SetTargetProperty(GetAnm, new PropertyPath("(Opacity)"));

            Storyboard.SetTarget(LoseAnm, text);
            Storyboard.SetTargetProperty(LoseAnm, new PropertyPath("(Opacity)"));

            GetStoryboard.Children.Add(GetAnm);
            LoseStoryboard.Children.Add(LoseAnm);

            MouseEnter += (_, _) => GetStoryboard.Begin();
            MouseLeave += (_, _) => LoseStoryboard.Begin();
        }
        #endregion


        #region StoryBoard

        private readonly Storyboard GetStoryboard = new Storyboard();
        private readonly Storyboard LoseStoryboard = new Storyboard();
        private readonly DoubleAnimation GetAnm = new DoubleAnimation() { Duration = TimeSpan.FromSeconds(0.15), To = 0 };
        private readonly DoubleAnimation LoseAnm = new DoubleAnimation() { Duration = TimeSpan.FromSeconds(0.15), To = 1 };

        #endregion


        #region ZoomChangeEvent
        /// <summary>
        /// 拡大率変更
        /// </summary>
        private void ZoomChange(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(Scene.TimeLineZoom))
            {
                return;
            }

            for (int frame = 0; frame < EaseList.Time.Count; frame++)
            {
                if (grid.Children[frame] is PackIcon icon)
                {
                    icon.Margin = new Thickness(Scene.GetCreateTimeLineViewModel().ToPixel(EaseList.Time[frame]), 0, 0, 0);
                }
            }

            Width = Scene.GetCreateTimeLineViewModel().ToPixel(EaseList.GetParent2().Length);
        }
        #endregion


        private void Add_Pos(object sender, RoutedEventArgs e) => AddCommand.Execute(nowframe);

        private void Delete_Click(object sender, RoutedEventArgs e) => RemoveCommand.Execute(EaseList.Time[grid.Children.IndexOf(select)]);

        private bool addtoggle;
        private int startpos;

        private PackIcon select;
        //相対的
        private int nowframe;

        #region MouseMoveEvent
        private void Mouse_Move(object sender, MouseEventArgs e)
        {
            if (addtoggle)
            {
                nowframe = Scene.GetCreateTimeLineViewModel().ToFrame(e.GetPosition(grid).X);

                addtoggle = false;
            }
            else if (select == null)
            {
                return;
            }
            else if (grid.Cursor == Cursors.SizeWE)
            {
                var now = Scene.GetCreateTimeLineViewModel().ToFrame(e.GetPosition(grid).X);
                var a = now - startpos + Scene.GetCreateTimeLineViewModel().ToFrame(select.Margin.Left);//相対

                select.Margin = new Thickness(Scene.GetCreateTimeLineViewModel().ToPixel(a), 0, 0, 0);

                startpos = now;
            }
        }
        #endregion


        private void Mouse_RightDown(object sender, MouseButtonEventArgs e) => addtoggle = true;


        #region KeyframeMouseDownイベント
        private void IconMouseDown(object sender, MouseButtonEventArgs e)
        {
            startpos = Scene.GetCreateTimeLineViewModel().ToFrame(e.GetPosition(grid).X);

            select = (PackIcon)sender;
            if (select.Cursor == Cursors.SizeWE)
            {
                grid.Cursor = Cursors.SizeWE;
            }

            for (int i = 0; i < grid.Children.Count; i++)
            {
                PackIcon icon = (PackIcon)grid.Children[i];

                if (icon != (sender as PackIcon))
                {
                    icon.MouseDown -= IconMouseDown;
                    icon.MouseUp -= IconMouseup;
                    icon.MouseMove -= IconMouseMove;
                    icon.MouseLeave -= IconMouseLeave;
                    icon.MouseLeftButtonUp -= IconMouseLeftUp;
                }
            }
        }
        #endregion

        #region KeyframeMouseUp
        private void IconMouseup(object sender, MouseButtonEventArgs e)
        {
            grid.Cursor = Cursors.Arrow;
            if (select != null)
            {
                select.Cursor = Cursors.Arrow;
            }

            for (int i = 0; i < grid.Children.Count; i++)
            {
                PackIcon icon = (PackIcon)grid.Children[i];

                if (icon != (sender as PackIcon))
                {
                    icon.MouseDown += IconMouseDown;
                    icon.MouseUp += IconMouseup;
                    icon.MouseMove += IconMouseMove;
                    icon.MouseLeave += IconMouseLeave;
                    icon.MouseLeftButtonUp += IconMouseLeftUp;
                }
            }
        }
        #endregion

        #region KeyframeMouseMove
        private void IconMouseMove(object sender, MouseEventArgs e)
        {
            select = (PackIcon)sender;

            ((PackIcon)sender).Cursor = Cursors.SizeWE;
            Scene.GetCreateTimeLineViewModel().KeyframeToggle = false;
        }
        #endregion

        #region KeyframeMouseLeave
        private void IconMouseLeave(object sender, MouseEventArgs e)
        {
            ((PackIcon)sender).Cursor = Cursors.Arrow;
            Scene.GetCreateTimeLineViewModel().KeyframeToggle = true;


            for (int i = 0; i < grid.Children.Count; i++)
            {
                PackIcon icon = (PackIcon)grid.Children[i];

                if (icon != (sender as PackIcon))
                {
                    icon.MouseDown -= IconMouseDown;
                    icon.MouseUp -= IconMouseup;
                    icon.MouseMove -= IconMouseMove;
                    icon.MouseLeave -= IconMouseLeave;
                    icon.MouseLeftButtonUp -= IconMouseLeftUp;

                    icon.MouseDown += IconMouseDown;
                    icon.MouseUp += IconMouseup;
                    icon.MouseMove += IconMouseMove;
                    icon.MouseLeave += IconMouseLeave;
                    icon.MouseLeftButtonUp += IconMouseLeftUp;
                }
            }
        }
        #endregion

        private void IconMouseLeftUp(object sender, MouseButtonEventArgs e) =>
            MoveCommand.Execute((grid.Children.IndexOf(select), Scene.GetCreateTimeLineViewModel().ToFrame(select.Margin.Left)));

        #region MouseUp
        private void Mouseup(object sender, MouseButtonEventArgs e)
        {
            grid.Cursor = Cursors.Arrow;
            if (select == null)
            {
                return;
            }

            if (select.Cursor == Cursors.SizeWE)
            {
                grid.Cursor = Cursors.SizeWE;
            }
        }
        #endregion

        #region MouseDown
        private void Mouse_Down(object sender, MouseButtonEventArgs e)
        {
            if (select == null)
            {
                return;
            }

            if (select.Cursor == Cursors.SizeWE)
            {
                startpos = Scene.GetCreateTimeLineViewModel().ToFrame(e.GetPosition(grid).X);
            }
        }
        #endregion

        #region MouseLeave
        private void Mouse_Leave(object sender, MouseEventArgs e)
        {
            grid.Cursor = Cursors.Arrow;
            if (select == null)
            {
                return;
            }

            if (select.Cursor == Cursors.SizeWE)
            {
                grid.Cursor = Cursors.SizeWE;
            }
        }
        #endregion


        #region メニュー作成
        private MenuItem CreateMenu()
        {
            MenuItem Delete = new MenuItem();

            //削除項目の設定
            var menu = new VirtualizingStackPanel() { Orientation = Orientation.Horizontal };
            menu.Children.Add(new PackIcon() { Kind = PackIconKind.Delete, Margin = new Thickness(5, 0, 5, 0) });
            menu.Children.Add(new TextBlock() { Text = Resource.Remove, Margin = new Thickness(20, 0, 5, 0) });
            Delete.Header = menu;

            Delete.Click += Delete_Click;

            return Delete;
        }
        #endregion
    }
}
