using System.Collections.Specialized;
using System.Reactive.Linq;
using System.Text.Json;
using Beutl.Collections;
using Beutl.Configuration;
using Beutl.Controls.PropertyEditors;
using Beutl.Services;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Tools;

public sealed class OutputPickerViewModel : IDisposable
{
    private const string PinnedProfilesKey = "OutputPicker.PinnedProfiles";
    private const string PinnedPresetsKey = "OutputPicker.PinnedPresets";

    private readonly ICoreList<OutputProfileItem> _profileSource;
    private readonly ICoreList<OutputPresetItem> _presetSource;
    private readonly HashSet<string> _pinnedProfiles;
    private readonly HashSet<string> _pinnedPresets;
    private readonly IDisposable _searchSubscription;

    public OutputPickerViewModel(
        ICoreList<OutputProfileItem> profileSource,
        ICoreList<OutputPresetItem> presetSource)
    {
        _profileSource = profileSource;
        _presetSource = presetSource;

        _pinnedProfiles = LoadPinned(PinnedProfilesKey);
        _pinnedPresets = LoadPinned(PinnedPresetsKey);

        _profileSource.CollectionChanged += OnProfileSourceChanged;
        _presetSource.CollectionChanged += OnPresetSourceChanged;

        _searchSubscription = SearchText
            .Skip(1)
            .Throttle(TimeSpan.FromMilliseconds(100))
            .ObserveOnUIDispatcher()
            .Subscribe(_ =>
            {
                RebuildProfiles();
                RebuildPresets();
            });

        RebuildProfiles();
        RebuildPresets();
    }

    public ReactiveCollection<PinnableOutputItem> ProfileItems { get; } = [];

    public ReactiveCollection<PinnableOutputItem> PresetItems { get; } = [];

    public ReactiveProperty<PinnableOutputItem?> SelectedProfile { get; } = new();

    public ReactiveProperty<PinnableOutputItem?> SelectedPreset { get; } = new();

    public ReactiveProperty<bool> ShowPresets { get; } = new(true);

    public ReactiveProperty<string?> SearchText { get; } = new();

    public void SetInitialSelection(OutputProfileItem? currentProfile)
    {
        if (currentProfile == null) return;
        SelectedProfile.Value = ProfileItems.FirstOrDefault(i => ReferenceEquals(i.UserData, currentProfile));
    }

    public void Pin(PinnableOutputItem item) => SetPinned(item, true);

    public void Unpin(PinnableOutputItem item) => SetPinned(item, false);

    private void SetPinned(PinnableOutputItem item, bool pinned)
    {
        switch (item.UserData)
        {
            case OutputProfileItem profile:
                if (pinned)
                    _pinnedProfiles.Add(profile.Context.Name.Value);
                else
                    _pinnedProfiles.Remove(profile.Context.Name.Value);
                Save(PinnedProfilesKey, _pinnedProfiles);
                RebuildProfiles();
                break;
            case OutputPresetItem preset:
                if (pinned)
                    _pinnedPresets.Add(preset.Name.Value);
                else
                    _pinnedPresets.Remove(preset.Name.Value);
                Save(PinnedPresetsKey, _pinnedPresets);
                RebuildPresets();
                break;
        }
    }

    public void Dispose()
    {
        _profileSource.CollectionChanged -= OnProfileSourceChanged;
        _presetSource.CollectionChanged -= OnPresetSourceChanged;
        _searchSubscription.Dispose();
    }

    private void OnProfileSourceChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildProfiles();

    private void OnPresetSourceChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildPresets();

    private void RebuildProfiles()
        => Rebuild(
            ProfileItems,
            SelectedProfile,
            _profileSource
                .Select(p => new PinnableOutputItem(
                    p.Context.Name.Value,
                    _pinnedProfiles.Contains(p.Context.Name.Value),
                    p)));

    private void RebuildPresets()
        => Rebuild(
            PresetItems,
            SelectedPreset,
            _presetSource
                .Select(p => new PinnableOutputItem(
                    p.Name.Value,
                    _pinnedPresets.Contains(p.Name.Value),
                    p)));

    // Refills target, filtered and sorted, keeping the selection on the item it held. The items
    // are only enumerated after the selection has been read and target cleared.
    private void Rebuild(
        ReactiveCollection<PinnableOutputItem> target,
        ReactiveProperty<PinnableOutputItem?> selection,
        IEnumerable<PinnableOutputItem> items)
    {
        var selected = selection.Value?.UserData;
        target.ClearOnScheduler();

        items = ApplyFilterAndSort(items);

        foreach (var item in items)
        {
            target.Add(item);
            if (selected != null && ReferenceEquals(item.UserData, selected))
            {
                selection.Value = item;
            }
        }
    }

    private IEnumerable<PinnableOutputItem> ApplyFilterAndSort(IEnumerable<PinnableOutputItem> items)
    {
        if (!string.IsNullOrWhiteSpace(SearchText.Value))
        {
            string[] segments = SearchText.Value.Split(' ')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();

            items = items.Where(item =>
                segments.All(s => item.DisplayName.Contains(s, StringComparison.OrdinalIgnoreCase)));
        }

        return items
            .OrderByDescending(i => i.IsPinned)
            .ThenBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase);
    }

    private static HashSet<string> LoadPinned(string key)
    {
        try
        {
            string json = Preferences.Default.Get(key, "[]");
            string[]? names = JsonSerializer.Deserialize<string[]>(json);
            return names == null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(names, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private static void Save(string key, HashSet<string> set)
    {
        string json = JsonSerializer.Serialize(set.ToArray());
        Preferences.Default.Set(key, json);
    }
}
