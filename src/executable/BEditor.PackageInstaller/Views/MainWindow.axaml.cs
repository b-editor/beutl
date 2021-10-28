using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using FluentAvalonia.Styling;

namespace BEditor.PackageInstaller.Views
{
    public sealed class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            Content = new MainPage();
            var thm = AvaloniaLocator.Current.GetService<FluentAvaloniaTheme>();

            if (OperatingSystem.IsWindows())
            {
                thm.ForceNativeTitleBarToTheme(this);
            }
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