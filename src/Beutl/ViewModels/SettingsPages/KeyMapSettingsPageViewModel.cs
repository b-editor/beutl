using Avalonia.Input;
using Avalonia.Threading;
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

    public ReactivePropertySlim<bool> IsExpanded { get; init; } = new(false);
}

public class KeyMapSettingsItem
{
    private readonly ContextCommandManager _commandManager;

    public KeyMapSettingsItem(ContextCommandEntry command, ContextCommandManager commandManager, int gestureIndex)
    {
        _commandManager = commandManager;
        Command = command;
        GestureIndex = gestureIndex;
        KeyGesture? gesture = ContextCommandManager.CurrentGestures(command).ElementAtOrDefault(gestureIndex);
        KeyGesture = new ReactiveProperty<KeyGesture?>(gesture);
    }

    public ContextCommandEntry Command { get; set; }

    /// <summary>
    /// Which of the command's same-platform gesture slots this row edits. A command binding
    /// several gestures (e.g. V and Escape) gets one row per slot, so remapping one binding
    /// leaves the others intact.
    /// </summary>
    public int GestureIndex { get; }

    internal string CommandName => Command.Definition.DisplayName ?? Command.Definition.Id;

    public string DisplayName => GestureIndex == 0 ? CommandName : $"{CommandName} ({GestureIndex + 1})";

    public ReactiveProperty<KeyGesture?> KeyGesture { get; set; }

    public string? ConflictMessage { get; internal set; }

    public ReactivePropertySlim<bool> ShowReset { get; } = new(false);

    public void SetKeyGesture(KeyGesture? gesture)
    {
        // Persist before updating the UI so a throwing ChangeKeyGesture leaves the displayed binding untouched.
        _commandManager.ChangeKeyGesture(Command, gesture, ContextCommandManager.CurrentPlatform, GestureIndex);
        KeyGesture.Value = gesture;
    }

    public void Reset() => _commandManager.ResetKeyGestures(Command, ContextCommandManager.CurrentPlatform);
}

public class KeyMapSettingsPageViewModel : PageContext, IDisposable
{
    private readonly ContextCommandManager _commandManager;
    private readonly ExtensionProvider _extensionProvider;
    private readonly IDisposable _searchSubscription;
    private KeyMapSettingsGroup[] _allGroups = [];
    private bool _disposed;

    public KeyMapSettingsPageViewModel(ContextCommandManager commandManager, ExtensionProvider extensionProvider)
    {
        _commandManager = commandManager;
        _extensionProvider = extensionProvider;
        _searchSubscription = SearchText.Subscribe(_ => FilterGroups());
        _commandManager.GesturesChanged += OnGesturesChanged;
        Refresh();
    }

    public ReactivePropertySlim<string?> SearchText { get; } = new();

    public KeyMapSettingsGroup[] Group => _allGroups;

    public ReactivePropertySlim<KeyMapSettingsGroup[]> VisibleGroups { get; } = new([]);

    public ReactivePropertySlim<bool> HasResults { get; } = new();

    private void OnGesturesChanged(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(Refresh);

    private void Refresh()
    {
        if (_disposed) return;

        var expanded = _allGroups.ToDictionary(group => group.Extension.GetType(), group => group.IsExpanded);
        _allGroups = _commandManager.GetDefinitions()
            .GroupBy(entry => entry.ExtensionType)
            .Select(entries =>
            {
                var extension = _extensionProvider.AllExtensions.FirstOrDefault(extension => extension.GetType() == entries.Key);
                if (extension is null)
                    return null;

                return new KeyMapSettingsGroup(extension, entries.SelectMany(entry =>
                    {
                        int slots = Math.Max(1, ContextCommandManager.CurrentGestures(entry).Count());
                        return Enumerable.Range(0, slots)
                            .Select(index => new KeyMapSettingsItem(entry, _commandManager, index));
                    }).ToArray())
                {
                    IsExpanded = expanded.GetValueOrDefault(entries.Key) ?? new(false)
                };
            })
            .OfType<KeyMapSettingsGroup>()
            .ToArray();

        foreach (KeyMapSettingsGroup group in _allGroups)
            MarkConflicts(group.Items);

        FilterGroups();
    }

    private static void MarkConflicts(KeyMapSettingsItem[] items)
    {
        // Two bindings only compete on the same extension and command context. Use Matches
        // for equivalences such as the keypad Add key and OemPlus.
        var boundItems = items.Where(item => item.KeyGesture.Value is not null)
            .GroupBy(item => (item.Command.Definition.KeyboardContextType, item.KeyGesture.Value!.KeyModifiers));

        foreach (var scope in boundItems)
        {
            foreach (KeyMapSettingsItem item in scope)
            {
                KeyGesture gesture = item.KeyGesture.Value!;
                var input = new KeyEventArgs { Key = gesture.Key, KeyModifiers = gesture.KeyModifiers };
                string? exclusiveGroup = item.Command.Definition.ExclusiveKeyboardGroup;
                string[] conflicts = scope
                    .Where(other => other.Command != item.Command && other.KeyGesture.Value!.Matches(input)
                        && (exclusiveGroup is null || other.Command.Definition.ExclusiveKeyboardGroup != exclusiveGroup))
                    .Select(other => other.CommandName).Distinct().ToArray();
                if (conflicts.Length > 0)
                    item.ConflictMessage = string.Format(SettingsStrings.Keymap_Conflict, string.Join(", ", conflicts));
            }
        }
    }

    private void FilterGroups()
    {
        string query = SearchText.Value?.Trim() ?? "";
        var visible = new List<KeyMapSettingsGroup>();
        foreach (KeyMapSettingsGroup group in _allGroups)
        {
            KeyMapSettingsGroup result = group;
            if (query.Length > 0)
            {
                var matches = Contains(group.Extension.DisplayName, query)
                    ? group.Items
                    : group.Items.Where(item =>
                        Contains(item.CommandName, query)
                        || Contains(item.Command.Definition.Id, query)
                        || Contains(item.Command.Definition.Description, query)
                        || Contains(item.KeyGesture.Value?.ToString(), query)).ToArray();
                if (matches.Length == 0) continue;

                result = new KeyMapSettingsGroup(group.Extension, matches) { IsExpanded = new(true) };
            }

            var seenCommands = new HashSet<ContextCommandEntry>();
            foreach (KeyMapSettingsItem item in result.Items)
            {
                item.ShowReset.Value = seenCommands.Add(item.Command)
                    && _commandManager.HasCustomKeyGestures(item.Command, ContextCommandManager.CurrentPlatform);
            }
            visible.Add(result);
        }

        VisibleGroups.Value = [.. visible];
        HasResults.Value = visible.Count > 0;
    }

    private static bool Contains(string? text, string query)
        => text?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _commandManager.GesturesChanged -= OnGesturesChanged;
        _searchSubscription.Dispose();
        SearchText.Dispose();
        VisibleGroups.Dispose();
        HasResults.Dispose();
    }
}
