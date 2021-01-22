using MahApps.Metro.Controls;

namespace BEditor.Views.CreateDialog
{
    /// <summary>
    /// CreateProjectWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class ProjectCreateDialog : MetroWindow
    {
        public ProjectCreateDialog()
        {
            InitializeComponent();
        }

        private void CloseClick(object sender, System.Windows.RoutedEventArgs e) => Close();
    }
}
