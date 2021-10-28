using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using BEditor.Controls;

namespace BEditor.Views.Timelines
{
    public sealed class SceneSettings : FluentWindow
    {
        public SceneSettings()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        public void CloseClick(object s, RoutedEventArgs e)
        {
            Close();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}