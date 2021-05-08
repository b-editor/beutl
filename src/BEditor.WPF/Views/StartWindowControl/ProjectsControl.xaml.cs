using System.Windows;
using System.Windows.Controls;

using BEditor.ViewModels.StartWindowControl;

namespace BEditor.Views.StartWindowControl
{
    /// <summary>
    /// ProjectsControl.xaml の相互作用ロジック
    /// </summary>
    public partial class ProjectsControl : UserControl
    {
        public ProjectsControl()
        {
            var d = new ProjectsControlViewModel();
            DataContext = d;

            d.Close += (_, _) =>
            {
                var win = Window.GetWindow(this);
                win.Close();
            };

            InitializeComponent();
        }
    }
}