using MahApps.Metro.Controls;

namespace BEditor {
    /// <summary>
    /// CreateProjectWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class CreateProjectWindow : MetroWindow {
        public CreateProjectWindow() {
            InitializeComponent();
        }

        private void CloseClick(object sender, System.Windows.RoutedEventArgs e) => Close();
    }
}
