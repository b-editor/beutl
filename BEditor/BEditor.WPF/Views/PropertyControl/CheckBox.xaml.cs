using System.Windows.Controls;

using BEditor.ViewModels.PropertyControl;
using BEditor.Views.CustomControl;
using BEditor.Core.Data.Property;
using BEditor.Core.Data.Primitive.Properties;
using System.Windows;

namespace BEditor.Views.PropertyControls
{
    /// <summary>
    /// CheckBox.xaml の相互作用ロジック
    /// </summary>
    public partial class CheckBox : UserControl, ICustomTreeViewItem
    {
        private readonly CheckProperty property;

        public CheckBox(CheckProperty check)
        {
            InitializeComponent();

            DataContext = new CheckPropertyViewModel(property = check);
        }

        public double LogicHeight => 32.5;

        private void BindClick(object sender, RoutedEventArgs e)
        {
            var window = new BindSettings()
            {
                DataContext = new BindSettingsViewModel<bool>(property)
            };
            window.ShowDialog();
        }
    }
}
