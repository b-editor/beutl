using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using BEditor.Controls;

namespace BEditor.Views.Properties
{
    public sealed class SetBinding : FluentWindow
    {
        public SetBinding()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        public void CloseButton(object s, RoutedEventArgs e)
        {
            Close();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}