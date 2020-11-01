using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

using BEditor.Models;
using BEditor.ViewModels.PropertyControl;
using BEditor.Views.CustomControl;

using BEditor.Core.Data;
using BEditor.Core.Data.PropertyData;

namespace BEditor.Views.PropertyControl {
    /// <summary>
    /// ColorAnimation.xaml の相互作用ロジック
    /// </summary>
    public partial class ColorAnimation : UserControl, ICustomTreeViewItem, ISizeChangeMarker {

        #region インターフェース

        public double LogicHeight {
            get {
                double h;
                if ((bool)togglebutton.IsChecked) {
                    h = OpenHeight;
                }
                else {
                    h = 32.5;
                }

                return h;
            }
        }

        public event EventHandler SizeChange;

        #endregion

        #region ColorAnimationメンバー

        private readonly ColorAnimationProperty ColorProperty;
        private double OpenHeight;

        #endregion

        public ColorAnimation(ColorAnimationProperty color) {
            InitializeComponent();
            DataContext = new ColorAnimationViewModel(color);
            ColorProperty = color;
            OpenHeight = (double)(OpenAnm.To = 32.5 * color.Value.Count + 10);

            Loaded += (_, _) => {
                color.Value.CollectionChanged += Value_CollectionChanged;


                OpenStoryboard.Children.Add(OpenAnm);
                CloseStoryboard.Children.Add(CloseAnm);

                Storyboard.SetTarget(OpenAnm, this);
                Storyboard.SetTargetProperty(OpenAnm, new PropertyPath("(Height)"));

                Storyboard.SetTarget(CloseAnm, this);
                Storyboard.SetTargetProperty(CloseAnm, new PropertyPath("(Height)"));
            };
        }

        #region ColorCollection変更イベント

        private void Value_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e) {
            if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Remove) {
                OpenHeight = (double)(OpenAnm.To = 32.5 * ColorProperty.Value.Count + 10);
                ListToggleClick(null, null);
            }
        }

        #endregion


        #region Animation

        private readonly Storyboard OpenStoryboard = new Storyboard();
        private readonly Storyboard CloseStoryboard = new Storyboard();
        private readonly DoubleAnimation OpenAnm = new DoubleAnimation() { Duration = TimeSpan.FromSeconds(0.25) };
        private readonly DoubleAnimation CloseAnm = new DoubleAnimation() { Duration = TimeSpan.FromSeconds(0.25), To = 32.5 };

        private void ListToggleClick(object sender, RoutedEventArgs e) {
            //開く
            if ((bool)togglebutton.IsChecked) {
                OpenStoryboard.Begin();
            }
            else {
                CloseStoryboard.Begin();
            }

            SizeChange?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        private void RectangleMouseDown(object sender, MouseButtonEventArgs e) {
            var rect = (Rectangle)sender;

            int index = AttachmentProperty.GetInt(rect);

            var color = ColorProperty;
            var d = new ColorDialog(color);

            d.col.Red = (byte)color.Value[index].R;
            d.col.Green = (byte)color.Value[index].G;
            d.col.Blue = (byte)color.Value[index].B;
            d.col.Alpha = (byte)color.Value[index].A;

            d.ok_button.Click += (_, _) => {
                UndoRedoManager.Do(new ColorAnimationProperty.ChangeColor(color, index, d.col.Red, d.col.Green, d.col.Blue, d.col.Alpha));
            };

            d.ShowDialog();
        }
    }
}
