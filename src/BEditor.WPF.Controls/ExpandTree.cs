using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BEditor.WPF.Controls
{
    public class ExpandTree : Control, ICustomTreeViewItem, ISizeChangeMarker
    {
        public static readonly DependencyProperty HeaderHeightProperty = DependencyProperty.Register("HeaderHeight", typeof(double), typeof(ExpandTree));
        public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register("Header", typeof(object), typeof(ExpandTree));
        public static readonly DependencyProperty HeaderColorProperty = DependencyProperty.Register("HeaderColor", typeof(SolidColorBrush), typeof(ExpandTree));
        public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.Register("IsExpanded", typeof(bool), typeof(ExpandTree), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, IsExpandedChanged));
        private Label? _content;
        private VirtualizingStackPanel? roothead;
        private readonly Storyboard OpenStoryboard = new Storyboard();
        private readonly Storyboard CloseStoryboard = new Storyboard();
        private readonly DoubleAnimation OpenAnm = new DoubleAnimation() { Duration = TimeSpan.FromSeconds(0.25) };
        private readonly DoubleAnimation CloseAnm = new DoubleAnimation() { Duration = TimeSpan.FromSeconds(0.25), To = 0 };


        static ExpandTree()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(ExpandTree), new FrameworkPropertyMetadata(typeof(ExpandTree)));
        }
        public ExpandTree()
        {
            SetResourceReference(HeaderColorProperty, "MaterialDesignCardBackground");
        }

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
                                    tmp += (child2 as FrameworkElement)?.Height ?? 0;
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
                            tmp += (child as FrameworkElement)?.Height ?? 0;
                        }

                        Debug.Assert(!double.IsNaN(tmp));

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
        public double HeaderHeight
        {
            get => (double)GetValue(HeaderHeightProperty);
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
        public SolidColorBrush HeaderColor
        {
            get => (SolidColorBrush)GetValue(HeaderColorProperty);
            set => SetValue(HeaderColorProperty, value);
        }
        public object? Content { get; set; }
        public int TreeStair { private get; set; }

        public event EventHandler? SizeChange;

        private static void IsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue == e.OldValue)
                return;

            ((ExpandTree)d).ExpanderUpdate();
        }
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
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _content = (Label)GetTemplateChild("_content");
            roothead = (VirtualizingStackPanel)GetTemplateChild("roothead");
            var button = (Button)GetTemplateChild("button");

            button.Click += Button_Click;

            _content.Content = Content;
            if (Content is FrameworkElement element)
            {
                element.SetBinding(WidthProperty, new Binding("ActualWidth") { Mode = BindingMode.OneWay, Source = _content });
            }

            roothead.Margin = new Thickness(HeaderHeight * TreeStair, 0, 0, 0);


            Storyboard.SetTarget(OpenAnm, _content);
            Storyboard.SetTargetProperty(OpenAnm, new PropertyPath("(Height)"));

            Storyboard.SetTarget(CloseAnm, _content);
            Storyboard.SetTargetProperty(CloseAnm, new PropertyPath("(Height)"));

            OpenStoryboard.Children.Add(OpenAnm);
            CloseStoryboard.Children.Add(CloseAnm);

            ExpanderUpdate();
        }
        private void Button_Click(object? sender, RoutedEventArgs e)
        {
            IsExpanded = !IsExpanded;
        }
        //子要素のサイズ変更イベント
        private void Value_SizeChange(object? sender, EventArgs e)
        {
            ExpanderUpdate();
        }

    }

    public interface ICustomTreeViewItem
    {
        public double LogicHeight { get; }
    }

    public interface ISizeChangeMarker
    {
        public event EventHandler? SizeChange;
    }
}
