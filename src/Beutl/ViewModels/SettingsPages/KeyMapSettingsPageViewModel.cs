using System.Runtime.InteropServices;
using Avalonia.Input;
using Beutl.Api.Services;
using Beutl.Controls.Navigation;
using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public class KeyMapSettingsGroup
{
    public KeyMapSettingsGroup(Extension extension, KeyMapSettingsItem[] items)
    {
        Extension = extension;
        Items = items;
    }

    public Extension Extension { get; }

    public KeyMapSettingsItem[] Items { get; }
}

public class KeyMapSettingsItem
{
    private readonly ContextCommandManager _commandManager;

    public KeyMapSettingsItem(ContextCommandEntry command, ContextCommandManager commandManager)
    {
        _commandManager = commandManager;
        Command = command;
        OSPlatform os = OperatingSystem.IsWindows() ? OSPlatform.Windows :
            OperatingSystem.IsLinux() ? OSPlatform.Linux :
            OSPlatform.OSX;
        var gesture = command.KeyGestures
            .FirstOrDefault(i => i.Platform == os)
            ?.KeyGesture;
        KeyGesture = new ReactiveProperty<KeyGesture?>(gesture);
    }

    public ContextCommandEntry Command { get; set; }

    public ReactiveProperty<KeyGesture?> KeyGesture { get; set; }

    public void SetKeyGesture(KeyGesture? gesture)
    {
        KeyGesture.Value = gesture;
        OSPlatform os = OperatingSystem.IsWindows() ? OSPlatform.Windows :
            OperatingSystem.IsLinux() ? OSPlatform.Linux :
            OperatingSystem.IsMacOS() ? OSPlatform.OSX :
            throw new NotSupportedException();
        _commandManager.ChangeKeyGesture(Command, gesture, os);
    }
}

public class KeyMapSettingsPageViewModel : PageContext
{
    private readonly ContextCommandManager _commandManager;

    public KeyMapSettingsPageViewModel(ContextCommandManager commandManager)
    {
        _commandManager = commandManager;
        Group = _commandManager.GetDefinitions()
            .GroupBy(i => i.ExtensionType)
            .Select(i =>
                new KeyMapSettingsGroup(
                    ExtensionProvider.Current.AllExtensions.First(j => j.GetType() == i.Key),
                    i.Select(j => new KeyMapSettingsItem(j, _commandManager)).ToArray()))
            .ToArray();
    }

    public KeyMapSettingsGroup[] Group { get; }
}
