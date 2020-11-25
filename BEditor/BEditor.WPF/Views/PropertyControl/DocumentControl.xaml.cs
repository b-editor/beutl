using System;
using System.Windows.Controls;

using BEditor.ViewModels.PropertyControl;
using BEditor.Views.CustomControl;

using BEditor.Core.Data.Property;
using BEditor.Core.Data.Primitive.Properties;
using System.Windows;
using System.Windows.Input;
using BEditor.Models;
using BEditor.Models.Extension;
using BEditor.Core.Data.Control;

namespace BEditor.Views.PropertyControls
{
    /// <summary>
    /// Document.xaml の相互作用ロジック
    /// </summary>
    public partial class Document : UserControl, ICustomTreeViewItem
    {
        private string oldvalue;
        private readonly DocumentProperty property;

        public Document(DocumentProperty document)
        {
            InitializeComponent();

            DataContext = new DocumentPropertyViewModel(this.property = document);
        }

        public double LogicHeight => property.HeightProperty ?? 125;

        private void BindClick(object sender, RoutedEventArgs e)
        {
            var window = new BindSettings()
            {
                DataContext = new BindSettingsViewModel<string>(property)
            };
            window.ShowDialog();
        }
        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            oldvalue = property.Value;
        }
        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var n = TextBox.Text;
            property.Text = oldvalue;

            Core.Command.CommandManager.Do(new DocumentProperty.TextChangeCommand(property, n));
        }
        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            property.Text = TextBox.Text;

            AppData.Current.Project.PreviewUpdate(property.GetParent2());
        }
    }
}
