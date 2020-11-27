using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using BEditor.Core.Data.Bindings;
using BEditor.Core.Extensions.ViewCommand;

namespace BEditor.Views.ToolControl.Default
{
    /// <summary>
    /// ObjectViewer.xaml の相互作用ロジック
    /// </summary>
    public partial class ObjectViewer : UserControl
    {
        public ObjectViewer()
        {
            InitializeComponent();
        }

        public static IEnumerable<string> Empty { get; } = Array.Empty<string>();

        private void GetPath_Click(object sender, RoutedEventArgs e)
        {
            if (TreeView.SelectedItem is IBindable bindable)
            {
                var path = bindable.GetString();
                Clipboard.SetText(path);
            }
            else
            {
                Message.Snackbar("IBindableでない");
            }
        }
    }
}
