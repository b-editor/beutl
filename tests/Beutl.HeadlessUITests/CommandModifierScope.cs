using Avalonia;
using Avalonia.Input;

namespace Beutl.HeadlessUITests;

// Sets the platform's command modifier, which every test after this one would otherwise inherit, until
// Dispose puts the previous one back.
internal sealed class CommandModifierScope : IDisposable
{
    private readonly KeyModifiers _previous;

    private CommandModifierScope(KeyModifiers previous) => _previous = previous;

    public static CommandModifierScope Use(KeyModifiers command)
    {
        var hotkeys = Application.Current!.PlatformSettings!.HotkeyConfiguration;
        var scope = new CommandModifierScope(hotkeys.CommandModifiers);
        hotkeys.CommandModifiers = command;
        return scope;
    }

    public void Dispose() => Application.Current!.PlatformSettings!.HotkeyConfiguration.CommandModifiers = _previous;
}
