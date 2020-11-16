using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

using BEditor.Models;
using BEditor.ViewModels.CustomControl;
using BEditor.ViewModels.PropertyControl;
using BEditor.Views;
using BEditor.Views.CustomControl;
using BEditor.ObjectModel.PropertyData;
using MaterialDesignThemes.Wpf;

namespace BEditor.Views.PropertyControls
{
    /// <summary>
    /// ColorPicker.xaml の相互作用ロジック
    /// </summary>
    public partial class ColorPicker : UserControl, ICustomTreeViewItem
    {
        public double LogicHeight => 42.5;
        private ColorDialog dialog
        {
            get
            {
                var color = DataContext as ColorPickerViewModel;
                var d = new ColorDialog(color);

                d.col.Red = color.Property.Red;
                d.col.Green = color.Property.Green;
                d.col.Blue = color.Property.Blue;
                d.col.Alpha = color.Property.Alpha;

                return d;
            }
        }

        public ColorPicker(ColorProperty color)
        {
            InitializeComponent();
            DataContext = new ColorPickerViewModel(color);
        }

        private void Palette_Click(object sender, RoutedEventArgs e)
        {
            dialog.ShowDialog();
        }
    }
}
