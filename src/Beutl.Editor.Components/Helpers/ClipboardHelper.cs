using Avalonia.Input.Platform;

namespace Beutl.Editor.Components.Helpers;

public static class ClipboardHelper
{
    // Test seam: the headless test app has no application lifetime to find a window through, so a
    // test supplies a window's clipboard here.
    internal static IClipboard? OverrideClipboard { get; set; }

    public static IClipboard? GetClipboard()
    {
        return OverrideClipboard ?? AppHelper.GetTopLevel()?.Clipboard;
    }
}
