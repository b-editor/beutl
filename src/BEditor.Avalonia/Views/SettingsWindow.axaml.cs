using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace BEditor.Views
{
    public class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            (Width, Height) = (800, 450);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}