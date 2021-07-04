using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace BEditor.Views
{
    public class FluentWindow : Window, IStyleable
    {
        public FluentWindow()
        {
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