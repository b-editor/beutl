using System.Windows;

using MahApps.Metro.Controls;

namespace BEditor.Views.PropertyControls
{
    /// <summary>
    /// BindSettings.xaml の相互作用ロジック
    /// </summary>
    public sealed partial class BindSettings : MetroWindow
    {
        public BindSettings(object datacontext)
        {
            DataContext = datacontext;
            InitializeComponent();
        }

        private void CloseButton(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}