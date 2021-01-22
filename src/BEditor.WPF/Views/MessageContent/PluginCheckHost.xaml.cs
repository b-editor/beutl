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

using BEditor.Core.Extensions;

namespace BEditor.Views.MessageContent
{
    /// <summary>
    /// PluginCheckHost.xaml の相互作用ロジック
    /// </summary>
    public partial class PluginCheckHost : DialogContent
    {
        public PluginCheckHost(object datacontext)
        {
            DataContext = datacontext;
            InitializeComponent();
        }

        public override ButtonType DialogResult { get; protected set; }

        public override event EventHandler? ButtonClicked;

        private void CloseClick(object sender, RoutedEventArgs e)
        {
            ButtonClicked?.Invoke(this, EventArgs.Empty);
        }
    }
}
