using System;

using Avalonia.Controls;
using Avalonia.Styling;

namespace BEditor.Views
{
    public class FluentWindow : Window, IStyleable
    {
        public FluentWindow()
        {
            SetValue(WindowConfig.SaveProperty, true);

            if (OperatingSystem.IsWindows())
            {
                ExtendClientAreaToDecorationsHint = true;
                ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
                ExtendClientAreaTitleBarHeightHint = -1;
            }
        }

        Type IStyleable.StyleKey => typeof(Window);
    }
}