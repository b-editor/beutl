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

            int index = _timeline.IndexOf(group);
            var children = group.Tools.ToArray();
            _timeline.RemoveAt(index);
            foreach (var child in children)
            {
                child.Group = null;
                _timeline.Insert(index++, child);
            }
            foreach (var child in children)
                GroupWithNeighbors(child);
        }
        else
        {
            GroupWithNeighbors(tool);
        }
    }

    private void GroupWithNeighbors(AgentToolItem tool)
    {
        if (!tool.CanGroup || tool.Group is not null)
            return;
        int index = _timeline.IndexOf(tool);
        if (index < 0)
            return;

        bool CanJoin(AgentTimelineItemViewModel item) =>
            item.TurnId == tool.TurnId && item is AgentToolGroupItem or AgentToolItem { CanGroup: true };
        int first = index;
        int last = index;
        while (first > 0 && CanJoin(_timeline[first - 1]))
            first--;
        while (last + 1 < _timeline.Count && CanJoin(_timeline[last + 1]))
            last++;
        if (first == last)
            return;

        var items = _timeline.Skip(first).Take(last - first + 1).ToArray();
        var group = items.OfType<AgentToolGroupItem>().FirstOrDefault() ?? new AgentToolGroupItem(tool.TurnId);
        var tools = items.SelectMany<AgentTimelineItemViewModel, AgentToolItem>(item => item is AgentToolGroupItem joined
            ? joined.Tools : [(AgentToolItem)item]).ToArray();
        for (int i = last; i > first; i--)
            _timeline.RemoveAt(i);
        if (!ReferenceEquals(_timeline[first], group))
            _timeline[first] = group;
        group.SetTools(tools);
    }
}
