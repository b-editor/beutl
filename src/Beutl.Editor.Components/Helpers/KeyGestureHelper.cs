using Avalonia;
using Avalonia.Input;

namespace Beutl.Editor.Components.Helpers;

public static class KeyGestureHelper
{
    public static KeyModifiers GetCommandModifier()
    {
        if (Application.Current is { PlatformSettings.HotkeyConfiguration: { } configuration })
        {
            return configuration.CommandModifiers;
        }
        else
        {
            return OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        }
    }
}
