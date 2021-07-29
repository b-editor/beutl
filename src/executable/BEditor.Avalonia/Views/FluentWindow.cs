using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;

namespace BEditor.Views
{
    public class FluentWindow : Window, IStyleable
    {
        private static WindowIcon? _icon;

        public FluentWindow()
        {
            var assets = AvaloniaLocator.Current.GetService<IAssetLoader>();
            var uri = new Uri("avares://beditor/Assets/Images/icon.ico");
            _icon ??= new WindowIcon(assets.Open(uri));
            Icon = _icon;
            SetValue(WindowConfig.SaveProperty, true);

            if (OperatingSystem.IsWindows())
            {
                TransparencyLevelHint = WindowTransparencyLevel.AcrylicBlur;
                ExtendClientAreaToDecorationsHint = true;
                ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
                ExtendClientAreaTitleBarHeightHint = -1;
            }
        }

        Type IStyleable.StyleKey => typeof(Window);
    }
}