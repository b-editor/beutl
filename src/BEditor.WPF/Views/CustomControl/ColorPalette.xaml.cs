using System.Windows;
using System.Windows.Controls;

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