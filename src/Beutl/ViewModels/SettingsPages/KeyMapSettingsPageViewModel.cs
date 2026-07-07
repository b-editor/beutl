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

    public KeyMapSettingsItem(ContextCommandEntry command, ContextCommandManager commandManager, int gestureIndex)
    {
        _commandManager = commandManager;
        Command = command;
        GestureIndex = gestureIndex;
        var gesture = command.KeyGestures
            .Where(i => i.Platform == CurrentPlatform)
            .ElementAtOrDefault(gestureIndex)
            ?.KeyGesture;
        KeyGesture = new ReactiveProperty<KeyGesture?>(gesture);
    }

    internal static OSPlatform CurrentPlatform => OperatingSystem.IsWindows() ? OSPlatform.Windows :
        OperatingSystem.IsLinux() ? OSPlatform.Linux :
        OSPlatform.OSX;

    public ContextCommandEntry Command { get; set; }

    /// <summary>
    /// Which of the command's same-platform gesture slots this row edits. A command binding
    /// several gestures (e.g. V and Escape) gets one row per slot, so remapping one binding
    /// leaves the others intact.
    /// </summary>
    public int GestureIndex { get; }

    public string? DisplayName => GestureIndex == 0
        ? Command.Definition.DisplayName
        : $"{Command.Definition.DisplayName} ({GestureIndex + 1})";

    public ReactiveProperty<KeyGesture?> KeyGesture { get; set; }

    public void SetKeyGesture(KeyGesture? gesture)
    {
        KeyGesture.Value = gesture;
        _commandManager.ChangeKeyGesture(Command, gesture, CurrentPlatform, GestureIndex);
    }
}

public class KeyMapSettingsPageViewModel : PageContext
{
    private readonly ContextCommandManager _commandManager;

    public KeyMapSettingsPageViewModel(ContextCommandManager commandManager, ExtensionProvider extensionProvider)
    {
        _commandManager = commandManager;
        Group = _commandManager.GetDefinitions()
            .GroupBy(i => i.ExtensionType)
            .Select(i =>
            {
                // A command definition can name an ExtensionType that has no registered extension
                // (e.g. a disabled plugin); skip those groups instead of throwing from First.
                var extension = extensionProvider.AllExtensions.FirstOrDefault(j => j.GetType() == i.Key);
                if (extension is null)
                {
                    return null;
                }

                return new KeyMapSettingsGroup(
                    extension,
                    i.SelectMany(j =>
                    {
                        int slots = Math.Max(1, j.KeyGestures.Count(g => g.Platform == KeyMapSettingsItem.CurrentPlatform));
                        return Enumerable.Range(0, slots)
                            .Select(index => new KeyMapSettingsItem(j, _commandManager, index));
                    }).ToArray());
            })
            .OfType<KeyMapSettingsGroup>()
            .ToArray();
    }

    public KeyMapSettingsGroup[] Group { get; }
}
