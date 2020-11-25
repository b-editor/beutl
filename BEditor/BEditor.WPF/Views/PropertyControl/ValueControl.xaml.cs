using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using BEditor.Core.Data.Control;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Models;
using BEditor.Models.Extension;
using BEditor.ViewModels.PropertyControl;
using BEditor.Views.CustomControl;

namespace BEditor.Views.PropertyControls
{
    /// <summary>
    /// ValueControl.xaml の相互作用ロジック
    /// </summary>
    public partial class ValueControl : UserControl, ICustomTreeViewItem
    {
        private float oldvalue;
        private readonly ValueProperty property;

        public ValueControl(ValueProperty property)
        {
            InitializeComponent();
            DataContext = new ValuePropertyViewModel(this.property = property);
        }

        public double LogicHeight => 32.5;

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            oldvalue = property.Value;
        }
        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (float.TryParse(textbox.Text, out float _out))
            {
                property.Value = oldvalue;

                Core.Command.CommandManager.Do(new ValueProperty.ChangeValueCommand(property, _out));
            }
        }
        private void TextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (textbox.IsKeyboardFocused && float.TryParse(textbox.Text, out var val))
            {
                int v = 10;//定数増え幅

                if (Keyboard.IsKeyDown(Key.LeftShift)) v = 1;

                val += e.Delta / 120 * v;

                property.Value = property.InRange(val);

                AppData.Current.Project.PreviewUpdate(property.GetParent2());

                e.Handled = true;
            }
        }
        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (float.TryParse(textbox.Text, out var val))
            {
                property.Value = property.InRange(val);

                AppData.Current.Project.PreviewUpdate(property.GetParent2());
            }
        }
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var window = new BindSettings()
            {
                DataContext = new BindSettingsViewModel<float>(property)
            };
            window.ShowDialog();
        }
    }
}
