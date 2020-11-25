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
    /// TextControl.xaml の相互作用ロジック
    /// </summary>
    public partial class TextControl : UserControl, ICustomTreeViewItem
    {
        private string oldvalue;
        private readonly TextProperty property;

        public TextControl(TextProperty property)
        {
            InitializeComponent();
            DataContext = new TextPropertyViewModel(this.property = property);
        }

        public double LogicHeight => 32.5;

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            oldvalue = property.Value;
        }
        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var n = textbox.Text;
            property.Value = oldvalue;

            Core.Command.CommandManager.Do(new TextProperty.ChangeTextCommand(property, n));
        }
        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            property.Value = textbox.Text;

            AppData.Current.Project.PreviewUpdate(property.GetParent2());
        }
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var window = new BindSettings()
            {
                DataContext = new BindSettingsViewModel<string>(property)
            };
            window.ShowDialog();
        }
    }
}
