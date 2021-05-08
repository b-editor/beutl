using System;
using System.Windows;

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

        public override IMessage.ButtonType DialogResult { get; protected set; }

        public override event EventHandler? ButtonClicked;

        private void CloseClick(object sender, RoutedEventArgs e)
        {
            ButtonClicked?.Invoke(this, EventArgs.Empty);
        }
    }
}