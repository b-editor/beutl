using Beutl.Collections.Pooled;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

public sealed partial class TimelineTabViewModel
{
    internal enum ElementSelectionKind
    {
        Following,
        FollowingAllLayers,
        Preceding,
        PrecedingAllLayers,
        Layer,
        AtPosition,
        Group,
        Invert
    }

    // These actions replace the selection once. Like range selection they include locked elements;
    // subsequent editing commands apply their usual lock and group rules.
    internal void SelectElements(ElementSelectionKind kind, MediaTime time, int layer, Element? reference = null)
    {
        if (kind == ElementSelectionKind.Group)
        {
            ApplySelection(DuplicateHelper.FindWithGroupSiblings(Scene, GetGroupSelectionSeeds(reference)).ToHashSet());
            return;
        }

        if (kind == ElementSelectionKind.Invert)
        {
            ApplySelection(Scene.Children.Except(_selection.SelectedElements).ToHashSet());
            return;
        }

        bool preceding = kind is ElementSelectionKind.Preceding or ElementSelectionKind.PrecedingAllLayers;
        bool following = kind is ElementSelectionKind.Following or ElementSelectionKind.FollowingAllLayers;
        int? targetLayer = kind is ElementSelectionKind.FollowingAllLayers or ElementSelectionKind.PrecedingAllLayers
            or ElementSelectionKind.AtPosition ? null : layer;
        using var candidates = new PooledList<Element>();
        Scene.ElementIndex.Query(
            preceding || kind == ElementSelectionKind.Layer ? MediaTime.MinValue : time,
            following || kind == ElementSelectionKind.Layer ? MediaTime.MaxValue : time,
            candidates, targetLayer);

        var selected = new HashSet<Element>(candidates.Count);
        foreach (Element element in candidates)
        {
            bool matches = kind switch
            {
                ElementSelectionKind.Following or ElementSelectionKind.FollowingAllLayers => element.Range.End > time,
                ElementSelectionKind.Preceding or ElementSelectionKind.PrecedingAllLayers => element.Start.Value <= time,
                ElementSelectionKind.Layer => true,
                ElementSelectionKind.AtPosition => element.Range.Contains(time),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            if (matches) selected.Add(element);
        }

        ApplySelection(selected);
    }

    internal bool CanSelectGroup(Element? reference) => GetGroupSelectionSeeds(reference).Any();

    private IEnumerable<Guid> GetGroupSelectionSeeds(Element? reference)
    {
        IEnumerable<Element> seeds = reference is null ? _selection.SelectedElements : [reference];
        return seeds.Select(element => element.Id.Value).Where(id => Scene.FindGroup(id) is not null);
    }
}
