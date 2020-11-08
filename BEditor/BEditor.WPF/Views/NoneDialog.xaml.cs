using System.Windows.Forms;

using BEditor.Views.MessageContent;

using MahApps.Metro.Controls;

namespace BEditor.Views {
    /// <summary>
    /// NoneWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class NoneDialog : MetroWindow {
        public NoneDialog(DialogContent content) {
            InitializeComponent();

            label.Content = content;

            content.ButtonClicked += (_, _) => {
                Close();
            };
        }
    }
}
