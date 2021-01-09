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
using System.Windows.Navigation;
using System.Windows.Shapes;

using BEditor.Core.Data.Primitive.Properties;

namespace BEditor.Views.SettingsControl.General
{
    /// <summary>
    /// IncludeFont.xaml の相互作用ロジック
    /// </summary>
    public partial class IncludeFont : UserControl
    {
        public IncludeFont()
        {
            InitializeComponent();
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                FontProperty.FontList.Clear();
                App.InitialFontManager();
            });
        }
    }
}
