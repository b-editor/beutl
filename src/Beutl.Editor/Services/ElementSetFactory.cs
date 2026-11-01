using System.Collections.Immutable;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Copies elements of a scene into an <see cref="ElementSet"/>, the form a template keeps several elements in.
/// </summary>
public static class ElementSetFactory
{
    /// <summary>
    /// A copy of <paramref name="elements"/> apart from <paramref name="scene"/>. Each copy starts and lies where it
    /// did relative to the earliest and lowest of them, and the scene's groups come along as far as the copies reach.
    /// </summary>
    public static ElementSet Create(Scene scene, IReadOnlyList<Element> elements)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(elements);
        if (elements.Count == 0)
            throw new ArgumentException("An element set needs at least one element.", nameof(elements));

        Element[] sources = [.. elements];
        ObjectRegenerator.Regenerate(sources, out Element[] copies);
        (TimeRange range, int minZIndex, _) = DuplicateHelper.ComputePlacementRange(copies);

        // The copies have Ids of their own, so the groups follow them the way a duplicate's do.
        var idMapping = new Dictionary<Guid, Guid>(sources.Length);
        for (int i = 0; i < sources.Length; i++)
        {
            idMapping[sources[i].Id.Value] = copies[i].Id.Value;
        }

        var set = new ElementSet { FrameSize = { Value = scene.FrameSize.Value } };
        set.Elements.AddRange(copies);
        set.Groups.AddRange(scene.Groups
            .Select(group => group.Where(idMapping.ContainsKey).Select(id => idMapping[id]).ToImmutableHashSet())
            .Where(group => group.Count >= 2));
        Move(set, -range.Start, -minZIndex);
        return set;
    }

    /// <summary>
    /// Moves every element of <paramref name="set"/> by the deltas. A matte that names its source layer moves with
    /// the elements, so it keeps the source it was saved with, or its distance from the set when the set does not
    /// hold that layer.
    /// </summary>
    internal static void Move(ElementSet set, MediaTime startDelta, int layerDelta)
    {
        foreach (Element element in set.Elements)
        {
            element.Start.Value += startDelta;
            element.ZIndex.Value += layerDelta;
            if (element.MatteSourceKind.Value == MatteSourceKind.Layer
                && element.MatteSourceZIndex.Value is { } source)
            {
                element.MatteSourceZIndex.Value = source + layerDelta;
            }
        }
    }
}
