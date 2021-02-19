using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

using BEditor.Data.Property;
using BEditor.Models;
using BEditor.Models.ColorTool;
using BEditor.ViewModels.CustomControl;
using BEditor.ViewModels.PropertyControl;
using BEditor.Views.CustomControl;
using BEditor.WPF.Controls;

using MahApps.Metro.Controls;

using ColorPicker = BEditor.WPF.Controls.ColorPicker;

namespace BEditor.Views
{
    /// <summary>
    /// ToolWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class ColorDialog : MetroWindow
    {
        public ColorDialog(ColorPickerViewModel color)
        {
            DataContext = color;
            InitializeComponent();
            ok_button.SetBinding(Button.CommandProperty, new Binding("Command") { Mode = BindingMode.OneWay });
            col.SetBinding(ColorPicker.UseAlphaProperty, new Binding("Metadata.Value.UseAlpha") { Mode = BindingMode.OneTime });
        }

        public ColorDialog(ColorAnimationProperty color)
        {
            DataContext = color;
            InitializeComponent();

            col.UseAlpha = color.PropertyMetadata?.UseAlpha ?? false;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ColPick_Dropper(object sender, RoutedEventArgs e)
        {
            ColorDropper.Run(x =>
            {
                col.Red = x.R;
                col.Green = x.G;
                col.Blue = x.B;
                col.Alpha = x.A;
            });
        }

        private void ColorPalette_SelectedEvent(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (sender is TreeView tree && tree.SelectedItem is ColorListProperty color)
            {
                col.Red = color.Red;
                col.Green = color.Green;
                col.Blue = color.Blue;
            }
        }
    }
}
