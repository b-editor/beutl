using MahApps.Metro.Controls;

namespace BEditor.Views.CreateDialog
{
    /// <summary>
    /// CreateSceneWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class SceneCreateDialog : MetroWindow
    {
        public SceneCreateDialog() => InitializeComponent();

        private void CloseClick(object sender, System.Windows.RoutedEventArgs e) => Close();
    }
}
