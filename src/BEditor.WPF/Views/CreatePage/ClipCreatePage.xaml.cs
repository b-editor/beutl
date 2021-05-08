using System.Windows;
using System.Windows.Controls;

namespace BEditor.Views.CreatePage
{
    /// <summary>
    /// ClipCreatePage.xaml の相互作用ロジック
    /// </summary>
    public sealed partial class ClipCreatePage : UserControl
    {
        public ClipCreatePage(object datacontext)
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