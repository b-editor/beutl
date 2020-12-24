using System.Windows;
using System.Windows.Controls;

using BEditor.Core.Data.Primitive.Properties;
using BEditor.Drawing;
using BEditor.ViewModels.PropertyControl;
using BEditor.Views.CustomControl;

namespace BEditor.Views.PropertyControls
{
    /// <summary>
    /// ColorPicker.xaml の相互作用ロジック
    /// </summary>
    public partial class ColorPicker : UserControl, ICustomTreeViewItem
    {
        private readonly ColorProperty property;

        public ColorPicker(ColorProperty color)
        {
            InitializeComponent();

            DataContext = new ColorPickerViewModel(property = color);
        }

        public double LogicHeight => 42.5;
        private ColorDialog Dialog
        {
            get
            {
                var color = DataContext as ColorPickerViewModel;
                var d = new ColorDialog(color);

                d.col.Red = color.Property.Color.R;
                d.col.Green = color.Property.Color.G;
                d.col.Blue = color.Property.Color.B;
                d.col.Alpha = color.Property.Color.A;

                return d;
            }
        }

        private void Palette_Click(object sender, RoutedEventArgs e) => Dialog.ShowDialog();
        private void BindClick(object sender, RoutedEventArgs e)
        {
            var window = new BindSettings()
            {
                DataContext = new BindSettingsViewModel<Color>(property)
            };
            window.ShowDialog();
        }
    }
}
