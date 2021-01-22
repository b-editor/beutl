
using MahApps.Metro.Controls;

namespace BEditor.Views.SettingsControl
{
    /// <summary>
    /// SettingsWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class SettingsWindow : MetroWindow
    {
        public SettingsWindow()
        {
            InitializeComponent();
        }

        private void MetroWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Settings.Default.Save();
        }
    }
}
