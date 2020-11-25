using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

using BEditor.Models;
using BEditor.Models.Extension;
using BEditor.ViewModels.PropertyControl;
using BEditor.Views.CustomControl;
using BEditor.Views.TimeLines;
using BEditor.Core.Data;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Control;

namespace BEditor.Views.PropertyControls
{
    /// <summary>
    /// EasePropertyControl.xaml の相互作用ロジック
    /// </summary>
    public partial class EaseControl : UserControl, ICustomTreeViewItem, ISizeChangeMarker
    {
        private readonly EaseProperty property;
        private double OpenHeight;
        private float oldvalue;
        private readonly Storyboard OpenStoryboard = new();
        private readonly Storyboard CloseStoryboard = new();
        private readonly DoubleAnimation OpenAnm = new() { Duration = TimeSpan.FromSeconds(0.25) };
        private readonly DoubleAnimation CloseAnm = new() { Duration = TimeSpan.FromSeconds(0.25), To = 32.5 };

        public EaseControl(EaseProperty property)
        {
            InitializeComponent();

            DataContext = new EasePropertyViewModel(property);
            this.property = property;

            OpenHeight = (double)(OpenAnm.To = 32.5 * property.Value.Count + 10);

            Loaded += (_, _) =>
            {
                this.property.Value.CollectionChanged += Value_CollectionChanged;

                OpenStoryboard.Children.Add(OpenAnm);
                CloseStoryboard.Children.Add(CloseAnm);

                Storyboard.SetTarget(OpenAnm, this);
                Storyboard.SetTargetProperty(OpenAnm, new PropertyPath("(Height)"));

                Storyboard.SetTarget(CloseAnm, this);
                Storyboard.SetTargetProperty(CloseAnm, new PropertyPath("(Height)"));
            };
        }

        public event EventHandler SizeChange;

        public double LogicHeight
        {
            get
            {
                double h;
                if ((bool)togglebutton.IsChecked)
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

        private void Value_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Remove)
            {
                OpenHeight = (double)(OpenAnm.To = 32.5 * property.Value.Count + 10);
                ListToggleClick(null, null);
            }
        }

        private void ListToggleClick(object sender, RoutedEventArgs e)
        {
            //開く
            if ((bool)togglebutton.IsChecked)
            {
                OpenStoryboard.Begin();
            }
            else
            {
                CloseStoryboard.Begin();
            }

            SizeChange?.Invoke(this, EventArgs.Empty);
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;

            TextBox tb = (TextBox)sender;


            int index = AttachmentProperty.GetInt(tb);

            oldvalue = property.Value[index];
        }
        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;

            TextBox tb = (TextBox)sender;

            int index = AttachmentProperty.GetInt(tb);

            if (float.TryParse(tb.Text, out float _out))
            {
                property.Value[index] = oldvalue;

                Core.Command.CommandManager.Do(new EaseProperty.ChangeValueCommand(property, index, _out));
            }
        }
        private void TextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            TextBox textBox = (TextBox)sender;

            if (textBox.IsKeyboardFocused && float.TryParse(textBox.Text, out var val))
            {
                int index = AttachmentProperty.GetInt(textBox);

                int v = 10;//定数増え幅

                if (Keyboard.IsKeyDown(Key.LeftShift)) v = 1;

                val += e.Delta / 120 * v;

                property.Value[index] = property.InRange(val);

                AppData.Current.Project.PreviewUpdate(property.GetParent2());

                e.Handled = true;
            }
        }
        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            TextBox textBox = (TextBox)sender;
            int index = AttachmentProperty.GetInt(textBox);


            if (float.TryParse(textBox.Text, out float _out))
            {
                property.Value[index] = property.InRange(_out);

                AppData.Current.Project.PreviewUpdate(property.GetParent2());
            }
        }
    }
}
