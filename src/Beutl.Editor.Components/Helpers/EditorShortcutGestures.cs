using System.Runtime.InteropServices;
using Avalonia.Input;

namespace Beutl.Editor.Components.Helpers;

internal static class EditorShortcutGestures
{
    internal const KeyModifiers AllModifiers =
        KeyModifiers.Alt | KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Meta;

    internal static ContextCommandKeyGesture[] Gestures(
        Key key, KeyModifiers required = KeyModifiers.None, KeyModifiers optional = KeyModifiers.None)
        =>
        [
            .. Expand(key, required, optional, OSPlatform.Windows),
            .. Expand(key, required, optional, OSPlatform.Linux),
            .. Expand(key, required, optional, OSPlatform.OSX)
        ];

    internal static ContextCommandKeyGesture[] WithCommand(
        Key key, KeyModifiers required = KeyModifiers.None, KeyModifiers optional = KeyModifiers.None)
        =>
        [
            .. Expand(key, required | KeyModifiers.Control, optional, OSPlatform.Windows),
            .. Expand(key, required | KeyModifiers.Control, optional, OSPlatform.Linux),
            .. Expand(key, required | KeyModifiers.Meta, optional, OSPlatform.OSX)
        ];

    internal static ContextCommandKeyGesture[] WithoutCommand(
        Key key, KeyModifiers required, KeyModifiers optional)
        =>
        [
            .. Expand(key, required, optional & ~KeyModifiers.Control, OSPlatform.Windows),
            .. Expand(key, required, optional & ~KeyModifiers.Control, OSPlatform.Linux),
            .. Expand(key, required, optional & ~KeyModifiers.Meta, OSPlatform.OSX)
        ];

    private static IEnumerable<ContextCommandKeyGesture> Expand(
        Key key, KeyModifiers required, KeyModifiers optional, OSPlatform platform)
    {
        // Keep the base gesture first for menu hints. Each additional chord is an editable slot.
        optional &= ~required;
        for (int mask = 0; mask <= (int)AllModifiers; mask++)
        {
            var extra = (KeyModifiers)mask;
            if ((extra & optional) == extra)
                yield return new ContextCommandKeyGesture(new KeyGesture(key, required | extra).ToString(), platform);
        }
    }
}
