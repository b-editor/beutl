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
using BEditor.Core.Data.PropertyData;
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

                d.col.Red = color.Property.Color.R;
                d.col.Green = color.Property.Color.G;
                d.col.Blue = color.Property.Color.B;
                d.col.Alpha = color.Property.Color.A;

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
