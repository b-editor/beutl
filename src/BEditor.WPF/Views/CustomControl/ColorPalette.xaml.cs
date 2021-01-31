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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace BEditor.Views.CustomControl
{
    /// <summary>
    /// ColorPalette.xaml の相互作用ロジック
    /// </summary>
    public partial class ColorPalette : UserControl
    {
        public ColorPalette()
        {
            InitializeComponent();
        }

        private void Selected(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            SelectedEvent?.Invoke(sender, e);
        }

        public event RoutedPropertyChangedEventHandler<object>? SelectedEvent;
    }
}
