using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Copies elements of a scene into an <see cref="ElementSet"/>, the form a template keeps several elements in.
/// </summary>
internal static class ElementSetFactory
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

        var set = new ElementSet { FrameSize = { Value = scene.FrameSize.Value } };
        set.Elements.AddRange(copies);
        // The copies have Ids of their own, so the groups follow them the way a duplicate's do.
        set.Groups.AddRange(DuplicateHelper.MapGroups(scene.Groups, sources, copies));
        ElementPlacementHelper.MoveTogether(set.Elements, -range.Start, -minZIndex);
        return set;
    }
}
