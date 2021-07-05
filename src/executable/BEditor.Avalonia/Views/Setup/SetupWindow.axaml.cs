using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BEditor.Views.Setup
{
    public partial class SetupWindow : FluentWindow
    {
        public SetupWindow()
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