using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

using MahApps.Metro.Controls;

namespace BEditor.Views.SettingsControls
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
