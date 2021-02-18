using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

using BEditor.ViewModels.PropertyControl;

using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.WPF.Controls;
using BEditor.Data;

namespace BEditor.Views.PropertyControl
{
    /// <summary>
    /// ColorAnimation.xaml の相互作用ロジック
    /// </summary>
    public partial class ColorAnimation : UserControl, ICustomTreeViewItem, ISizeChangeMarker
    {
        private readonly ColorAnimationProperty ColorProperty;
        private double OpenHeight;
        private readonly Storyboard OpenStoryboard = new Storyboard();
        private readonly Storyboard CloseStoryboard = new Storyboard();
        private readonly DoubleAnimation OpenAnm = new DoubleAnimation() { Duration = TimeSpan.FromSeconds(0.25) };
        private readonly DoubleAnimation CloseAnm = new DoubleAnimation() { Duration = TimeSpan.FromSeconds(0.25), To = 32.5 };

        public ColorAnimation(ColorAnimationProperty color)
        {
            DataContext = new ColorAnimationViewModel(color);
            InitializeComponent();
            ColorProperty = color;
            OpenHeight = (double)(OpenAnm.To = 32.5 * color.Value.Count + 10);

            Loaded += (_, _) =>
            {
                color.Value.CollectionChanged += Value_CollectionChanged;


                OpenStoryboard.Children.Add(OpenAnm);
                CloseStoryboard.Children.Add(CloseAnm);

                Storyboard.SetTarget(OpenAnm, this);
                Storyboard.SetTargetProperty(OpenAnm, new PropertyPath("(Height)"));

                Storyboard.SetTarget(CloseAnm, this);
                Storyboard.SetTargetProperty(CloseAnm, new PropertyPath("(Height)"));
            };
        }

        public double LogicHeight
        {
            get
            {
                double h;
                if ((bool)togglebutton.IsChecked!)
                {
                    h = OpenHeight;
                }
                else
                {
                    h = 32.5;
                }

                return h;
            }
        }

        public event EventHandler? SizeChange;

        
        private void Value_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Remove)
            {
                OpenHeight = (double)(OpenAnm.To = 32.5 * ColorProperty.Value.Count + 10);
                ListToggleClick(null, null);
            }
        }
        private void ListToggleClick(object? sender, RoutedEventArgs? e)
        {
            //開く
            if ((bool)togglebutton.IsChecked!)
            {
                OpenStoryboard.Begin();
            }
            else
            {
                CloseStoryboard.Begin();
            }

            SizeChange?.Invoke(this, EventArgs.Empty);
        }
        private void RectangleMouseDown(object sender, MouseButtonEventArgs e)
        {
            var rect = (System.Windows.Shapes.Rectangle)sender;

            int index = AttachmentProperty.GetInt(rect);

            var color = ColorProperty;
            var d = new ColorDialog(color);

            d.col.Red = color.Value[index].R;
            d.col.Green = color.Value[index].G;
            d.col.Blue = color.Value[index].B;
            d.col.Alpha = color.Value[index].A;

            d.ok_button.Click += (_, _) =>
            {
                ColorProperty.ChangeColor(index, Color.FromARGB(d.col.Alpha, d.col.Red, d.col.Green, d.col.Blue)).Execute();
            };

            d.ShowDialog();
        }
    }
}
