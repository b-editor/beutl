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

namespace BEditor.Views.CreatePage
{
    /// <summary>
    /// EffectAddPage.xaml の相互作用ロジック
    /// </summary>
    public sealed partial class EffectAddPage : UserControl
    {
        public EffectAddPage(object datacontext)
        {
            DataContext = datacontext;
            InitializeComponent();
        }

        private void CloseClick(object sender, RoutedEventArgs e)
        {
            Window.GetWindow(this).Close();
        }
    }
}
