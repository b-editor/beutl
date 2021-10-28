using Avalonia;
using Avalonia.Markup.Xaml;

using BEditor.Controls;

namespace BEditor.Views.Dialogs
{
    public partial class AboutBEditor : FluentWindow
    {
        public AboutBEditor()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}