using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Builds the multi-element inputs for <see cref="IElementResizeService.Roll"/> /
/// <see cref="IElementResizeService.Slide"/> from a set of group / selection members, so a
/// trim gesture on one clip moves every grouped cut or block with it. Model-level so the
/// collection rules are unit-testable; the Timeline View resolves the returned elements to
/// its ViewModels for the drag preview.
/// </summary>
public static class TrimGroupCollector
{
    /// <summary>
    /// The rollable pairs among <paramref name="members"/> whose cut sits exactly at
    /// <paramref name="boundary"/>: for each member ending there, the clip starting there
    /// on its layer becomes the back; for each member starting there, the clip ending
    /// there becomes the front. The partner may itself be outside
    /// <paramref name="members"/> (rolling against an ungrouped neighbour is a valid cut).
    /// Members with no adjacent partner are skipped (their edge simply is not a cut, so
    /// nothing desynchronizes); a pair whose both sides are members is collected once.
    /// Returns <see langword="null"/> when any collected pair has a locked side — that cut
    /// is aligned but immovable, so rolling the rest would desynchronize the grouped cuts;
    /// <see cref="IElementResizeService.Slide"/>'s lanes apply the same all-or-nothing rule.
    /// </summary>
    public static IReadOnlyList<ElementTrimPair>? CollectRollPairs(
        Scene scene, IReadOnlyList<Element> members, TimeSpan boundary)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(members);

        var pairs = new List<ElementTrimPair>();
        var seenFronts = new HashSet<Element>();
        var seenMembers = new HashSet<Element>();
        foreach (Element member in members)
        {
            if (!seenMembers.Add(member)) continue;
            if (!scene.Children.Contains(member)) continue;

            Element? front = null;
            Element? back = null;
            if (member.Range.End == boundary)
            {
                (front, back) = (member, FindStartingAt(scene, member.ZIndex, boundary, member));
            }
            else if (member.Start == boundary)
            {
                (front, back) = (FindEndingAt(scene, member.ZIndex, boundary, member), member);
            }

            if (front is null || back is null) continue;
            if (scene.IsElementLocked(front) || scene.IsElementLocked(back)) return null;
            if (!seenFronts.Add(front)) continue;

            pairs.Add(new ElementTrimPair(front, back));
        }

        return pairs;
    }

    /// <summary>
    /// The slide lanes formed by <paramref name="members"/>: per layer, the members must
    /// form one contiguous run with an adjacent, unlocked clip on each side (front and
    /// back), which may be outside <paramref name="members"/>. Returns
    /// <see langword="null"/> when any layer cannot slide — members not contiguous, no
    /// adjacent front or back, or a locked participant — because a partial slide would
    /// desync the grouped block; <see cref="IElementResizeService.Slide"/> applies the
    /// same all-or-nothing rule.
    /// </summary>
    public static IReadOnlyList<ElementSlideLane>? CollectSlideLanes(
        Scene scene, IReadOnlyList<Element> members)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(members);

        var lanes = new List<ElementSlideLane>();
        foreach (IGrouping<int, Element> lane in members.Distinct()
                     .GroupBy(m => m.ZIndex)
                     .OrderBy(g => g.Key))
        {
            Element[] middles = lane.OrderBy(m => m.Start).ToArray();
            foreach (Element middle in middles)
            {
                if (!scene.Children.Contains(middle)) return null;
                if (scene.IsElementLocked(middle)) return null;
            }

            for (int i = 1; i < middles.Length; i++)
            {
                if (middles[i - 1].Range.End != middles[i].Start) return null;
            }

            Element? front = FindEndingAt(scene, lane.Key, middles[0].Start, middles[0]);
            Element? back = FindStartingAt(scene, lane.Key, middles[^1].Range.End, middles[^1]);
            if (front is null || back is null) return null;
            if (scene.IsElementLocked(front) || scene.IsElementLocked(back)) return null;

            lanes.Add(new ElementSlideLane(front, middles, back));
        }

        return lanes.Count == 0 ? null : lanes;
    }

    private static Element? FindStartingAt(Scene scene, int zIndex, TimeSpan start, Element exclude)
    {
        foreach (Element item in scene.Children)
        {
            if (item != exclude && item.ZIndex == zIndex && item.Start == start) return item;
        }

        return null;
    }

    private static Element? FindEndingAt(Scene scene, int zIndex, TimeSpan end, Element exclude)
    {
        foreach (Element item in scene.Children)
        {
            if (item != exclude && item.ZIndex == zIndex && item.Range.End == end) return item;
        }

        return null;
    }
}
