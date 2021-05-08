using System.Windows;
using System.Windows.Input;

namespace BEditor.Views
{
    /// <summary>
    /// SplashWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
        }

        private void Grid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }
    }
}