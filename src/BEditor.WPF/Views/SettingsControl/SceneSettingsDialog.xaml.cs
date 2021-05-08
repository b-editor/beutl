using System.Windows;

using MahApps.Metro.Controls;

namespace BEditor.Views.SettingsControl
{
    /// <summary>
    /// SceneSettingsDialog.xaml の相互作用ロジック
    /// </summary>
    public partial class SceneSettingsDialog : MetroWindow
    {
        public SceneSettingsDialog()
        {
            InitializeComponent();
        }

        private void CloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}