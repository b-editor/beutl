using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using BEditor.Data;

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

        private async void TreeView_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (Mouse.LeftButton == MouseButtonState.Pressed)
            {
                await Task.Delay(10);

                if (TreeView.SelectedItem is not EffectMetadata select || select.Type == null) return;

                // ドラッグ開始
                var dataObject = new DataObject(select);
                DragDrop.DoDragDrop(App.Current.MainWindow, dataObject, DragDropEffects.Copy);
            }
        }
    }
}