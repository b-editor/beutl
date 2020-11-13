using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;

using BEditor.Views.PropertyControls;

namespace BEditor.Views.CustomControl
{

    /// <summary>
    /// CustomTree.xaml の相互作用ロジック
    /// </summary>
    public partial class CustomTreeView : UserControl, ICustomTreeViewItem, ISizeChangeMarker
    {

        public static readonly DependencyProperty HeaderHeightProperty = DependencyProperty.Register("HeaderHeight", typeof(float), typeof(CustomTreeView));
        public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register("Header", typeof(object), typeof(CustomTreeView));
        public static readonly DependencyProperty HeaderColorProperty = DependencyProperty.Register("HeaderColor", typeof(SolidColorBrush), typeof(CustomTreeView));
        public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register("IsExpanded", typeof(bool), typeof(CustomTreeView), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, IsExpandedChanged));

        private static void IsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue == e.OldValue)
                return;

            (d as CustomTreeView).ExpanderUpdate();
        }

        /// <summary>
        /// 強制的にExpanderをアップデート
        /// </summary>
        public void ExpanderUpdate()
        {
            if (this.IsExpanded)
            {
                var h = this.LogicHeight - this.HeaderHeight;
                if (h != 0)
                {
                    this.OpenAnm.To = h;
                    this.OpenStoryboard.Begin();
                }
            }
            else
            {
                this.CloseStoryboard.Begin();
            }

            this.SizeChange?.Invoke(this, EventArgs.Empty);
        }

        public CustomTreeView()
        {
            InitializeComponent();

            SetResourceReference(HeaderColorProperty, "MaterialDesignCardBackground");

            Storyboard.SetTarget(OpenAnm, _content);
            Storyboard.SetTargetProperty(OpenAnm, new PropertyPath("(Height)"));

            Storyboard.SetTarget(CloseAnm, _content);
            Storyboard.SetTargetProperty(CloseAnm, new PropertyPath("(Height)"));

            OpenStoryboard.Children.Add(OpenAnm);
            CloseStoryboard.Children.Add(CloseAnm);
        }

        #region ICustomTreeViewItem
        public double LogicHeight
        {
            get
            {
                double tmp = HeaderHeight;

                if (Content is Panel stack && IsExpanded)
                {
                    foreach (var child in stack.Children)
                    {
                        if (child is ICustomTreeViewItem propertyControl)
                        {
                            tmp += propertyControl.LogicHeight;
                        }
                        else if (child is Panel stack2)
                        {
                            foreach (var child2 in stack2.Children)
                            {
                                if (child2 is ICustomTreeViewItem propertyControl2)
                                {
                                    tmp += propertyControl2.LogicHeight;
                                }
                                else
                                {
                                    tmp += (child2 as FrameworkElement)?.ActualHeight ?? 0;
                                }

                                if (child2 is ISizeChangeMarker value_2)
                                {
                                    value_2.SizeChange -= Value_SizeChange;

                                    value_2.SizeChange += Value_SizeChange;
                                }
                            }
                        }
                        else
                        {
                            tmp += (child as FrameworkElement)?.ActualHeight ?? 0;
                        }

                        if (child is ISizeChangeMarker value_)
                        {
                            value_.SizeChange -= Value_SizeChange;

                            value_.SizeChange += Value_SizeChange;
                        }
                    }
                }

                return tmp;
            }
        }

        //子要素のサイズ変更イベント
        private void Value_SizeChange(object sender, EventArgs e)
        {
            ExpanderUpdate();
        }

        #endregion


        #region プロパティ
        public float HeaderHeight
        {
            get => (float)GetValue(HeaderHeightProperty);
            set => SetValue(HeaderHeightProperty, value);
        }

        public object Header
        {
            get => GetValue(HeaderProperty);
            set => SetValue(HeaderProperty, value);
        }

        public bool IsExpanded
        {
            get => (bool)GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }

        /// <summary>
        /// 自動でサイズがCustomTreeのActualWidthになります
        /// </summary>
        public new object Content
        {
            get => _content.Content;
            set
            {
                _content.Content = value;
                if (value is FrameworkElement element)
                {
                    element.SetBinding(WidthProperty, new Binding("ActualWidth") { Mode = BindingMode.OneWay, Source = _content });
                }
            }
        }


        public SolidColorBrush HeaderColor
        {
            get => (SolidColorBrush)GetValue(HeaderColorProperty);
            set => SetValue(HeaderColorProperty, value);
        }

        public int TreeStair
        {
            set => roothead.Margin = new Thickness(HeaderHeight * value, 0, 0, 0);
        }
        #endregion

        public event EventHandler SizeChange;

        private Storyboard OpenStoryboard = new Storyboard();
        private Storyboard CloseStoryboard = new Storyboard();
        private DoubleAnimation OpenAnm = new DoubleAnimation() { Duration = TimeSpan.FromSeconds(0.25) };
        private DoubleAnimation CloseAnm = new DoubleAnimation() { Duration = TimeSpan.FromSeconds(0.25), To = 0 };


        private void Button_Click(object sender, RoutedEventArgs e)
        {
            IsExpanded = !IsExpanded;
        }
    }

    public interface ICustomTreeViewItem
    {
        public double LogicHeight { get; }
    }

    public interface ISizeChangeMarker
    {
        public event EventHandler SizeChange;
    }
}
