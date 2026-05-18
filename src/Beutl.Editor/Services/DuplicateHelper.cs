using System.Collections.Immutable;
using Beutl.Logging;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Utilities;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Services;

public static class DuplicateHelper
{
    private const string ElementFileExtension = "belm";
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(DuplicateHelper));

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
            if (group.Overlaps(ids))
            {
                ids.UnionWith(group);
            }
        }

        return ids;
    }

    /// <summary>
    /// Returns true when placing duplicates of <paramref name="sourceElements"/> at the
    /// given anchor would land on a source clip (same ZIndex, intersecting TimeRange).
    /// Used by Alt+drag to skip the duplicate when the user has not moved far enough
    /// for the copy to clear the originals.
    /// </summary>
    public static bool WouldOverlapSources(
        IReadOnlyList<Element> sourceElements,
        TimeSpan anchorStart,
        int anchorZIndex)
    {
        ArgumentNullException.ThrowIfNull(sourceElements);
        if (sourceElements.Count == 0) return false;

        (TimeSpan minStart, int minZIndex) = ComputeMinOrigin(sourceElements);

        foreach (Element src in sourceElements)
        {
            TimeSpan newStart = src.Start - minStart + anchorStart;
            int newZIndex = src.ZIndex - minZIndex + anchorZIndex;
            var newRange = new TimeRange(newStart, src.Length);

            foreach (Element other in sourceElements)
            {
                if (other.ZIndex == newZIndex && other.Range.Intersects(newRange))
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

        TimeSpan minStart = TimeSpan.MaxValue;
        TimeSpan maxEnd = TimeSpan.MinValue;
        int minZIndex = int.MaxValue;
        int maxZIndex = int.MinValue;
        foreach (Element e in elements)
        {
            if (e.Start < minStart) minStart = e.Start;
            TimeSpan end = e.Range.End;
            if (end > maxEnd) maxEnd = end;
            if (e.ZIndex < minZIndex) minZIndex = e.ZIndex;
            if (e.ZIndex > maxZIndex) maxZIndex = e.ZIndex;
        }

        return (new TimeRange(minStart, maxEnd - minStart), minZIndex, maxZIndex);
    }

    private static (TimeSpan MinStart, int MinZIndex) ComputeMinOrigin(IReadOnlyList<Element> elements)
    {
        TimeSpan minStart = TimeSpan.MaxValue;
        int minZIndex = int.MaxValue;
        foreach (Element e in elements)
        {
            if (e.Start < minStart) minStart = e.Start;
            if (e.ZIndex < minZIndex) minZIndex = e.ZIndex;
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
    /// Two phases:
    /// <list type="bullet">
    /// <item>Phase 1 stages every element to disk. On failure, staged files are deleted
    /// best-effort and the scene is left untouched.</item>
    /// <item>Phase 2 remaps groups and calls <see cref="Scene.AddChild"/>. Not atomic:
    /// a failure here can leave partial children and staged files behind.</item>
    /// </list>
    /// </remarks>
    public static void PlaceDuplicates(
        Scene scene,
        Element[] newElements,
        Element[] sourceElements,
        TimeSpan anchorStart,
        int anchorZIndex)
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

        (TimeSpan minStart, int minZIndex) = ComputeMinOrigin(newElements);

        var stagedFiles = new List<string>(newElements.Length);
        try
        {
            foreach (Element newElement in newElements)
            {
                newElement.Start = newElement.Start - minStart + anchorStart;
                newElement.ZIndex = newElement.ZIndex - minZIndex + anchorZIndex;

                Uri uri = RandomFileNameGenerator.GenerateUri(scene.Uri, ElementFileExtension);
                CoreSerializer.StoreToUri(newElement, uri);
                stagedFiles.Add(uri.LocalPath);
            }
        }
        catch (Exception originalEx)
        {
            // Best-effort cleanup. Don't mask the original exception with delete errors,
            // but log them so orphan files can be tracked.
            foreach (string path in stagedFiles)
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception deleteEx)
                {
                    s_logger.LogWarning(
                        deleteEx,
                        "Failed to delete staged duplicate file during rollback; orphan file may remain. Path={Path} OriginalCause={Original}",
                        path, originalEx.Message);
                }
            }

            throw;
        }

        var idMapping = new Dictionary<Guid, Guid>(sourceElements.Length);
        for (int i = 0; i < sourceElements.Length; i++)
        {
            idMapping[sourceElements[i].Id] = newElements[i].Id;
        }

        List<ImmutableHashSet<Guid>> newGroups = scene.Groups
            .Select(g => g.Where(id => idMapping.ContainsKey(id))
                .Select(id => idMapping[id])
                .ToImmutableHashSet())
            .Where(g => g.Count >= 2)
            .ToList();
        if (newGroups.Count > 0)
        {
            scene.Groups.AddRange(newGroups);
        }

        foreach (Element newElement in newElements)
        {
            scene.AddChild(newElement);
        }
    }
}
