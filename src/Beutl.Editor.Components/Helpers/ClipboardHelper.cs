using Avalonia.Input.Platform;

namespace Beutl.Editor.Components.Helpers;

public static class ClipboardHelper
{
    public static IClipboard? GetClipboard()
    {
        return AppHelper.GetTopLevel()?.Clipboard;
    }
}
