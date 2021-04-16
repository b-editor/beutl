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