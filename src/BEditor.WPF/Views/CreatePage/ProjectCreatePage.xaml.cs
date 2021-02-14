using System.Windows;
using System.Windows.Controls;

using MahApps.Metro.Controls;

namespace BEditor.Views.CreatePage
{
    /// <summary>
    /// CreateProjectPage.xaml の相互作用ロジック
    /// </summary>
    public partial class ProjectCreatePage : UserControl
    {
        public ProjectCreatePage()
        {
            InitializeComponent();
        }

        private void CloseClick(object sender, RoutedEventArgs e) 
            => Window.GetWindow(this)?.Close();
    }
}
