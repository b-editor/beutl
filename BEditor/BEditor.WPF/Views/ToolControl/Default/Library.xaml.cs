using System.Windows.Controls;
using System.Windows.Input;

namespace BEditor.Views.ToolControl.Default
{
    /// <summary>
    /// Library.xaml の相互作用ロジック
    /// </summary>
    public partial class Library : UserControl
    {
        public Library() => InitializeComponent();

        private void TreeView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0)
            {
                for (int i = 0; i < 4; i++)
                {
                    scrollViewer.LineUp();
                }
            }
            else
            {
                for (int i = 0; i < 4; i++)
                {
                    scrollViewer.LineDown();
                }
            }
        }
    }
}
