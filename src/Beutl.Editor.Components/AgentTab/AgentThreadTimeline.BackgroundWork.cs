using System.Collections.ObjectModel;
using System.ComponentModel;
using Beutl.Editor.Components.AgentTab.ViewModels;
using FluentIcons.Common;

namespace Beutl.Editor.Components.AgentTab;

internal sealed partial class AgentThreadTimeline
{
    private Guid? _backgroundWorkTurnId;

    public AgentBackgroundWorkGroup BackgroundCommands { get; } = new(AgentStrings.BackgroundCommands, Icon.WindowConsole);
    public AgentBackgroundWorkGroup Subagents { get; } = new(AgentStrings.Subagents, Icon.People);
    public bool HasSubagents => Subagents.HasItems;
    public bool HasBackgroundWork => BackgroundCommands.HasItems || HasSubagents;
    public string BackgroundWorkCount => (BackgroundCommands.Items.Count + Subagents.Items.Count).ToString(CultureInfo.CurrentCulture);

    private void ResetBackgroundWork(Guid? turnId)
    {
        if (turnId == _backgroundWorkTurnId)
            return;

        _backgroundWorkTurnId = turnId;
        BackgroundCommands.Clear();
        Subagents.Clear();
        NotifyBackgroundWork();
    }

    private void TrackBackgroundWork(AgentToolItem item)
    {
        if (item.IsBackgroundWork && item.Group is not null)
            UpdateGrouping(item);
        if (item.TurnId is null || (item.TurnId != _backgroundWorkTurnId && _backgroundWorkTurnId is not null))
            return;

        _backgroundWorkTurnId ??= item.TurnId;
        bool commandsChanged = BackgroundCommands.Track(item, item.IsBackground && !item.IsSubagent);
        bool subagentsChanged = Subagents.Track(item, item.IsSubagent);
        if (commandsChanged || subagentsChanged)
            NotifyBackgroundWork();
    }

    private void NotifyBackgroundWork() => Notify(nameof(HasSubagents), nameof(HasBackgroundWork), nameof(BackgroundWorkCount));
}

internal sealed class AgentBackgroundWorkGroup(string label, Icon icon) : AgentBindable
{
    private readonly ObservableCollection<AgentToolItem> _items = [];

    public IReadOnlyList<AgentToolItem> Items => _items;
    public string Label => label;
    public Icon Icon => icon;
    public bool HasItems => _items.Count > 0;
    public bool IsRunning => _items.Any(item => item.IsInProgress);
    public string Counter
    {
        get
        {
            int running = _items.Count(item => item.IsInProgress);
            int count = running > 0 ? running : _items.Count(item => item.IsSuccessful);
            return $"{count}/{_items.Count}";
        }
    }
    public string? Summary => (_items.LastOrDefault(item => item.IsInProgress) ?? _items.LastOrDefault()) is { } current
        ? $"{current.Status} · {current.Title}" : null;

    public bool Track(AgentToolItem item, bool include)
    {
        bool contains = _items.Contains(item);
        if (contains == include)
            return false;

        if (include)
        {
            _items.Add(item);
            item.PropertyChanged += OnItemChanged;
        }
        else
        {
            item.PropertyChanged -= OnItemChanged;
            _items.Remove(item);
        }
        Notify(nameof(HasItems));
        NotifySummary();
        return true;
    }

    public void Clear()
    {
        foreach (var item in _items)
            item.PropertyChanged -= OnItemChanged;
        _items.Clear();
        Notify(nameof(HasItems));
        NotifySummary();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(AgentToolItem.Status) or nameof(AgentToolItem.Title))
            NotifySummary();
    }

    private void NotifySummary() => Notify(nameof(IsRunning), nameof(Counter), nameof(Summary));
}
