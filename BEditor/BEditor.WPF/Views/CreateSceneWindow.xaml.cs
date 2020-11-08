using MahApps.Metro.Controls;

namespace BEditor.Views {
    /// <summary>
    /// CreateSceneWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class CreateSceneWindow : MetroWindow {
        public CreateSceneWindow() => InitializeComponent();

        private void CloseClick(object sender, System.Windows.RoutedEventArgs e) => Close();
    }
}
