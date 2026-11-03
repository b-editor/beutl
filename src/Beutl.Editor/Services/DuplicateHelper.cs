using System.Collections.Immutable;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public static class DuplicateHelper
{
    /// <summary>
    /// Expands the seed set to include every member of any group that overlaps it,
    /// so partial group selections still duplicate the whole group.
    /// </summary>
    public static HashSet<Guid> ExpandWithGroupSiblings(
        IEnumerable<Guid> seedIds,
        IEnumerable<ImmutableHashSet<Guid>> groups)
    {
        var ids = new HashSet<Guid>(seedIds);
        foreach (ImmutableHashSet<Guid> group in groups)
        {
            // Walk the group, not ids: Overlaps walks its argument, which costs groups × ids.
            if (group.Any(ids.Contains))
            {
                ids.UnionWith(group);
            }
        }

        return ids;
    }

    // The scene's elements among the seed and the rest of their groups, in scene order.
    internal static Element[] FindWithGroupSiblings(Scene scene, IEnumerable<Guid> seedIds)
    {
        HashSet<Guid> ids = ExpandWithGroupSiblings(seedIds, scene.Groups);
        return [.. scene.Children.Where(element => ids.Contains(element.Id.Value))];
    }

    // The groups as far as the copies reach, naming the copies. copies[i] is the copy of sources[i].
    internal static List<ImmutableHashSet<Guid>> MapGroups(
        IEnumerable<ImmutableHashSet<Guid>> groups,
        IReadOnlyList<Element> sources,
        IReadOnlyList<Element> copies)
    {
        var idMapping = new Dictionary<Guid, Guid>(sources.Count);
        for (int i = 0; i < sources.Count; i++)
        {
            idMapping[sources[i].Id.Value] = copies[i].Id.Value;
        }

        return groups
            .Select(g => g.Where(id => idMapping.ContainsKey(id))
                .Select(id => idMapping[id])
                .ToImmutableHashSet())
            .Where(g => g.Count >= 2)
            .ToList();
    }

    /// <summary>
    /// Returns true when placing duplicates of <paramref name="sourceElements"/> at the
    /// given anchor would land on a source clip (same ZIndex, intersecting TimeRange).
    /// Used by Alt+drag to skip the duplicate when the user has not moved far enough
    /// for the copy to clear the originals.
    /// </summary>
    public static bool WouldOverlapSources(
        IReadOnlyList<Element> sourceElements,
        MediaTime anchorStart,
        int anchorZIndex)
    {
        ArgumentNullException.ThrowIfNull(sourceElements);
        if (sourceElements.Count == 0) return false;

        (MediaTime minStart, int minZIndex) = ComputeMinOrigin(sourceElements);

        foreach (Element src in sourceElements)
        {
            MediaTime newStart = src.Start.Value - minStart + anchorStart;
            int newZIndex = src.ZIndex.Value - minZIndex + anchorZIndex;
            var newRange = new TimeRange(newStart, src.Length.Value);

            foreach (Element other in sourceElements)
            {
                if (other.ZIndex.Value == newZIndex && other.Range.Intersects(newRange))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the seed range for placement search. Uses the latest Range.End (not the
    /// latest Start) so a short trailing element does not shrink the range and let the
    /// spiral search land on top of a longer leading element.
    /// </summary>
    public static (TimeRange Range, int MinZIndex, int MaxZIndex) ComputePlacementRange(
        IReadOnlyList<Element> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        if (elements.Count == 0)
        {
            throw new ArgumentException("Elements must not be empty.", nameof(elements));
        }

        MediaTime minStart = MediaTime.MaxValue;
        MediaTime maxEnd = MediaTime.MinValue;
        int minZIndex = int.MaxValue;
        int maxZIndex = int.MinValue;
        foreach (Element e in elements)
        {
            if (e.Start.Value < minStart) minStart = e.Start.Value;
            MediaTime end = e.Range.End;
            if (end > maxEnd) maxEnd = end;
            if (e.ZIndex.Value < minZIndex) minZIndex = e.ZIndex.Value;
            if (e.ZIndex.Value > maxZIndex) maxZIndex = e.ZIndex.Value;
        }

        return (new TimeRange(minStart, maxEnd - minStart), minZIndex, maxZIndex);
    }

    private static (MediaTime MinStart, int MinZIndex) ComputeMinOrigin(IReadOnlyList<Element> elements)
    {
        MediaTime minStart = MediaTime.MaxValue;
        int minZIndex = int.MaxValue;
        foreach (Element e in elements)
        {
            if (e.Start.Value < minStart) minStart = e.Start.Value;
            if (e.ZIndex.Value < minZIndex) minZIndex = e.ZIndex.Value;
        }

        return (minStart, minZIndex);
    }

    /// <summary>
    /// Places duplicated elements onto the scene. <paramref name="sourceElements"/> and
    /// <paramref name="newElements"/> use a positional zip mapping
    /// (<c>sourceElements[i] -&gt; newElements[i]</c>); <paramref name="scene"/>.<see cref="Scene.Uri"/>
    /// must be non-null.
    /// </summary>
    /// <remarks>
    /// Writes no file: each element gets its file when the scene is next saved. Not atomic: a failure can leave
    /// partial children behind. Each duplicate is fitted around what it overlaps as it is added.
    /// </remarks>
    public static void PlaceDuplicates(
        Scene scene,
        Element[] newElements,
        Element[] sourceElements,
        MediaTime anchorStart,
        int anchorZIndex)
        => PlaceDuplicates(scene, newElements, sourceElements, anchorStart, anchorZIndex, ElementOverlapHandling.Auto);

    /// <inheritdoc cref="PlaceDuplicates(Scene, Element[], Element[], MediaTime, int)"/>
    /// <remarks>
    /// Writes no file, and is not atomic. <paramref name="overlapHandling"/> applies to each duplicate as it is
    /// added: a caller that found the anchor free for the whole set passes <see cref="ElementOverlapHandling.Allow"/>,
    /// so duplicates that overlap each other on one layer keep that overlap.
    /// </remarks>
    internal static void PlaceDuplicates(
        Scene scene,
        Element[] newElements,
        Element[] sourceElements,
        MediaTime anchorStart,
        int anchorZIndex,
        ElementOverlapHandling overlapHandling,
        IEnumerable<ImmutableHashSet<Guid>>? sourceGroups = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(newElements);
        ArgumentNullException.ThrowIfNull(sourceElements);

        if (newElements.Length == 0) return;
        if (newElements.Length != sourceElements.Length)
        {
            throw new ArgumentException(
                "newElements and sourceElements must have the same length (positional id mapping).",
                nameof(sourceElements));
        }

        if (scene.Uri is null)
        {
            throw new InvalidOperationException(
                "Cannot place duplicates: scene has no Uri. Save the project before duplicating.");
        }

        (MediaTime minStart, int minZIndex) = ComputeMinOrigin(newElements);
        List<(Element Matte, Element Source)> copiedMattes = FindCopiedMatteSources(newElements);

        foreach (Element newElement in newElements)
        {
            newElement.Start.Value = newElement.Start.Value - minStart + anchorStart;
            newElement.ZIndex.Value = newElement.ZIndex.Value - minZIndex + anchorZIndex;
            newElement.Uri = ElementFileNaming.GetUri(scene.Uri, newElement.Id.Value);
        }

        List<ImmutableHashSet<Guid>> newGroups = MapGroups(sourceGroups ?? scene.Groups, sourceElements, newElements);
        if (newGroups.Count > 0)
        {
            scene.Groups.AddRange(newGroups);
        }

        foreach (Element newElement in newElements)
        {
            scene.AddChild(newElement, overlapHandling);
        }

        // A matte follows a source copied with it, to the layer that copy ended up on: fitting it around what it
        // overlaps can move it off its layer. Any other layer the matte names still holds its source.
        foreach ((Element matte, Element source) in copiedMattes)
        {
            matte.MatteSourceZIndex.Value = source.ZIndex.Value;
        }
    }

    // The copies whose matte names a layer where another copy is at the same time, and that other copy.
    private static List<(Element Matte, Element Source)> FindCopiedMatteSources(Element[] copies)
    {
        var result = new List<(Element Matte, Element Source)>();
        foreach (Element copy in copies)
        {
            if (copy.MatteSourceKind.Value == MatteSourceKind.Layer
                && copy.MatteSourceZIndex.Value is { } layer
                && copies.FirstOrDefault(other => other != copy
                    && other.ZIndex.Value == layer
                    && other.Range.Intersects(copy.Range)) is { } source)
            {
                result.Add((copy, source));
            }
        }

        return result;
    }
}
