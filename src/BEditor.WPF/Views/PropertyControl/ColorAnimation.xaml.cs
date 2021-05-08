using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.ViewModels.PropertyControl;
using BEditor.WPF.Controls;

namespace BEditor.Views.PropertyControl
{
    /// <summary>
    /// ColorAnimation.xaml の相互作用ロジック
    /// </summary>
    public sealed partial class ColorAnimation : UserControl, ICustomTreeViewItem, ISizeChangeMarker, IDisposable
    {
        private ColorAnimationProperty _property;
        private double _openHeight;
        private readonly Storyboard _openStoryboard = new();
        private readonly Storyboard _closeStoryboard = new();
        private readonly DoubleAnimation _openAnm = new() { Duration = TimeSpan.FromSeconds(0.25) };
        private readonly DoubleAnimation _closeAnm = new() { Duration = TimeSpan.FromSeconds(0.25), To = 32.5 };

        public ColorAnimation(ColorAnimationProperty property)
        {
            DataContext = new ColorAnimationViewModel(property);
            InitializeComponent();
            _property = property;
            _openHeight = (double)(_openAnm.To = 32.5 * property.Value.Count + 10);

            property.Value.CollectionChanged += Value_CollectionChanged;


            _openStoryboard.Children.Add(_openAnm);
            _closeStoryboard.Children.Add(_closeAnm);

            Storyboard.SetTarget(_openAnm, this);
            Storyboard.SetTargetProperty(_openAnm, new PropertyPath("(Height)"));

            Storyboard.SetTarget(_closeAnm, this);
            Storyboard.SetTargetProperty(_closeAnm, new PropertyPath("(Height)"));
        }

        public double LogicHeight
        {
            get
            {
                double h;
                if ((bool)togglebutton.IsChecked!)
                {
                    h = _openHeight;
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
                _openHeight = (double)(_openAnm.To = 32.5 * _property.Value.Count + 10);
                ListToggleClick(null, null);
            }
        }
        private void ListToggleClick(object? sender, RoutedEventArgs? e)
        {
            //開く
            if ((bool)togglebutton.IsChecked!)
            {
                _openStoryboard.Begin();
            }
            else
            {
                _closeStoryboard.Begin();
            }

            SizeChange?.Invoke(this, EventArgs.Empty);
        }
        private void RectangleMouseDown(object sender, MouseButtonEventArgs e)
        {
            var rect = (System.Windows.Shapes.Rectangle)sender;

            int index = AttachmentProperty.GetInt(rect);

            var color = _property;
            var d = new ColorDialog(color);

            d.col.Red = color.Value[index].R;
            d.col.Green = color.Value[index].G;
            d.col.Blue = color.Value[index].B;
            d.col.Alpha = color.Value[index].A;

            d.ok_button.Click += (_, _) =>
            {
                _property.ChangeColor(index, Color.FromARGB(d.col.Alpha, d.col.Red, d.col.Green, d.col.Blue)).Execute();
            };

            d.ShowDialog();
        }
        public void Dispose()
        {
            if (DataContext is IDisposable disposable)
            {
                disposable.Dispose();
            }

            DataContext = null;
            _property.Value.CollectionChanged -= Value_CollectionChanged;
            _property = null!;

            GC.SuppressFinalize(this);
        }
    }
}