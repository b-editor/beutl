using Beutl.Editor.Components.AgentTab.ViewModels;

namespace Beutl.Editor.Components.AgentTab;

internal sealed partial class AgentThreadTimeline
{
    private void UpdateGrouping(AgentToolItem tool)
    {
        if (tool.Group is { } group)
        {
            if (tool.CanGroup)
            {
                group.RefreshSummary();
                return;
            }

            Ungroup(group);
        }
        else
        {
            GroupWithNeighbors(tool);
        }
    }

    private void Ungroup(AgentToolGroupItem group)
    {
        int index = _timeline.IndexOf(group);
        if (index < 0)
            return;

        var children = group.Items.ToArray();
        _timeline.RemoveAt(index);
        group.ClearItems();
        foreach (var child in children)
            _timeline.Insert(index++, child);

        foreach (var child in children)
        {
            if (child is AgentToolItem tool)
                GroupWithNeighbors(tool);
        }
    }

    private void GroupWithNeighbors(AgentTimelineItemViewModel anchor)
    {
        if (anchor.TurnId is not { } turnId || !CanJoinGroup(anchor, turnId) || IsGrouped(anchor))
            return;
        int index = _timeline.IndexOf(anchor);
        if (index < 0)
            return;

        int first = index;
        int last = index;
        while (first > 0 && CanJoinGroup(_timeline[first - 1], turnId))
            first--;
        while (last + 1 < _timeline.Count && CanJoinGroup(_timeline[last + 1], turnId))
            last++;
        while (first <= last && _timeline[first] is AgentReasoningItem)
            first++;
        while (last >= first && _timeline[last] is AgentReasoningItem)
            last--;
        if (first >= last)
            return;

        var span = _timeline.Skip(first).Take(last - first + 1).ToArray();
        var group = span.OfType<AgentToolGroupItem>().FirstOrDefault() ?? new AgentToolGroupItem(turnId);
        var items = span.SelectMany<AgentTimelineItemViewModel, AgentTimelineItemViewModel>(item => item is AgentToolGroupItem joined
            ? joined.Items : [item]).ToArray();
        for (int i = last; i > first; i--)
            _timeline.RemoveAt(i);
        if (!ReferenceEquals(_timeline[first], group))
            _timeline[first] = group;
        foreach (var joined in span.OfType<AgentToolGroupItem>())
        {
            if (!ReferenceEquals(joined, group))
                joined.ClearItems();
        }
        group.SetItems(items);
    }

    private static bool CanJoinGroup(AgentTimelineItemViewModel item, Guid turnId) =>
        item.TurnId == turnId
        && item is AgentToolGroupItem or AgentToolItem { CanGroup: true } or AgentReasoningItem { CanGroup: true };

    private static bool IsGrouped(AgentTimelineItemViewModel item) =>
        item is AgentToolItem { Group: not null } or AgentReasoningItem { Group: not null };
}
