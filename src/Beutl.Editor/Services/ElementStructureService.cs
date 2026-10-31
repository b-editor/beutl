using System.Collections.Immutable;
using Beutl.Animation;
using Beutl.Audio;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public sealed class ElementStructureService : IElementStructureService
{
    private readonly HistoryManager _historyManager;

    public ElementStructureService(HistoryManager historyManager)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
    }

    // The next save deletes the element files, and saves them again after an undo.
    public void Delete(Scene scene, IReadOnlyList<Element> elements, bool ripple = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(elements);
        Element[] editable = RemovableElements(scene, elements);
        if (editable.Length == 0) return;

        RippleHelper.RemoveAndShiftAfter(scene, editable, ripple, scene.RemoveChild);

        scene.RemoveElementsFromGroups(editable.Select(e => e.Id.Value).ToArray());

        _historyManager.Commit(CommandNames.DeleteElement);
    }

    public void Split(Scene scene, IReadOnlyList<Element> targets, MediaTime at)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0 || scene.Uri is null) return;

        if (!targets.Any(target => !scene.IsElementLocked(target) && LeavesAFrameOnEachSide(scene, target, at)))
            return;
        _historyManager.ExecuteInTransaction(() => SplitCore(scene, targets, at), CommandNames.SplitElement);
    }

    // Shared by single-cut Razor and batch edits; the caller owns the history transaction.
    internal static IReadOnlyList<Element> SplitCore(Scene scene, IReadOnlyList<Element> targets, MediaTime at,
        SplitBatch? batch = null)
    {
        var groupUpdates = new Dictionary<int, List<Guid>>();
        var added = new List<Element>();
        foreach (Element target in targets)
        {
            if (scene.IsElementLocked(target) || !LeavesAFrameOnEachSide(scene, target, at)) continue;

            Element backward = SplitElement(scene, target, at, batch);
            added.Add(backward);

            int groupIndex = FindGroupIndex(scene, target.Id.Value);
            if (groupIndex >= 0)
            {
                if (!groupUpdates.TryGetValue(groupIndex, out List<Guid>? newIds))
                {
                    newIds = [];
                    groupUpdates.Add(groupIndex, newIds);
                }

                newIds.Add(backward.Id.Value);
            }
        }

        if (batch == null) InsertSplitGroups(scene, groupUpdates);
        else
            foreach (List<Guid> ids in groupUpdates.Values)
                if (ids.Count >= 2) batch.PendingGroups.Add([.. ids]);
        return added;
    }

    private static Element SplitElement(Scene scene, Element target, MediaTime at, SplitBatch? batch)
    {
        MediaTime forwardDuration = at - target.Start.Value;
        MediaTime backwardDuration = target.Length.Value - forwardDuration;

        List<SplitSource> sources = CaptureSplitSources(target, forwardDuration, batch);

        ObjectRegenerator.Regenerate(target, out Element backward);

        backward.Start.Value = at;
        backward.Length.Value = backwardDuration;
        backward.EnterTransition.Value = null;
        // Detached group children subscribe to inherited timing only when published.
        // Establish that timing before calculating the new fragment's source positions.
        foreach (SoundGroup group in backward.Objects.OfType<SoundGroup>()) RestoreGroupTiming(group);

        ShiftLocalKeyFrames(backward, -forwardDuration);

        backward.Uri = ElementFileNaming.GetUri(scene.Uri!, backward.Id.Value);
        target.ExitTransition.Value = null;
        if (batch != null)
        {
            // A partition stays inside its original clip; no placement search or scene notification is needed.
            target.Length.Value = forwardDuration;
        }
        else
        {
            scene.MoveChild(target.ZIndex.Value, target.Start.Value, forwardDuration, target);
            scene.AddChild(backward);
        }
        ApplySplitSourceDeltas(backward, sources, forwardDuration, batch);
        foreach (SplitSource source in sources)
            source.Source.NotifySplitted(false, MediaTime.Zero, -backwardDuration);
        return backward;
    }

    // Whether a cut at the frame `at` leaves a frame on each side; a clip end within a tick of a frame counts as that frame.
    internal static bool LeavesAFrameOnEachSide(Scene scene, Element target, MediaTime at)
    {
        MediaTime frame = SceneTimeRangeService.GetFrameDuration(scene);
        return at - SceneTimeRangeService.SnapToFrame(scene, target.Start.Value) >= frame
               && SceneTimeRangeService.SnapToFrame(scene, target.Range.End) - at >= frame;
    }

    private static int FindGroupIndex(Scene scene, Guid id)
    {
        for (int i = 0; i < scene.Groups.Count; i++)
        {
            if (scene.Groups[i].Contains(id))
            {
                return i;
            }
        }

        return -1;
    }

    // Inserting from the highest index down keeps the indices of the groups still to be processed valid.
    private static void InsertSplitGroups(Scene scene, Dictionary<int, List<Guid>> groupUpdates)
    {
        foreach ((int index, List<Guid> value) in groupUpdates.OrderByDescending(x => x.Key))
        {
            ImmutableHashSet<Guid> newGroup = [.. value];
            if (newGroup.Count >= 2)
            {
                scene.Groups.Insert(index + 1, newGroup);
            }
        }
    }

    public void Group(Scene scene, IReadOnlyCollection<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(ids);
        ids = FilterStrandingIds(scene, FilterUnlockedIds(scene, ids));
        if (ids.Count == 0) return;

        // Remove ids from existing groups first — with a single id this acts as
        // "ungroup this element".
        bool removed = scene.RemoveElementsFromGroups(ids);

        bool created = false;
        if (ids.Count >= 2)
        {
            ImmutableHashSet<Guid> newGroup = [.. ids];
            if (!scene.Groups.Any(g => g.SetEquals(newGroup)))
            {
                scene.Groups.Add(newGroup);
                created = true;
            }
        }

        // Commit only when something changed; an unconditional Commit would
        // sweep unrelated pending operations into a phantom undo entry.
        if (removed || created)
        {
            _historyManager.Commit(CommandNames.GroupElements);
        }
    }

    public void Ungroup(Scene scene, IReadOnlyCollection<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(ids);
        ids = FilterStrandingIds(scene, FilterUnlockedIds(scene, ids));
        if (ids.Count == 0) return;

        if (scene.RemoveElementsFromGroups(ids))
        {
            _historyManager.Commit(CommandNames.UngroupElements);
        }
    }

    // Refuses to pull an editable member out of a group when that would leave the group with only a
    // locked survivor: disbanding it would silently change the locked member's grouping. Such ids are
    // dropped from the request so the group is left intact. Only structural regroups (Ungroup/Group)
    // use this — removal ops (Delete/Cut) must always prune a removed element's id.
    private static IReadOnlyCollection<Guid> FilterStrandingIds(Scene scene, IReadOnlyCollection<Guid> ids)
    {
        if (ids.Count == 0) return ids;

        HashSet<Guid>? lockedIds = CollectLockedIds(scene);
        if (lockedIds is null) return ids;

        var idSet = new HashSet<Guid>(ids);
        HashSet<Guid>? refused = null;
        foreach (ImmutableHashSet<Guid> group in scene.Groups)
        {
            int survivors = 0;
            bool lockedSurvivor = false;
            foreach (Guid member in group)
            {
                if (idSet.Contains(member)) continue;
                survivors++;
                lockedSurvivor |= lockedIds.Contains(member);
            }

            if (survivors < 2 && lockedSurvivor)
            {
                foreach (Guid member in group)
                {
                    if (idSet.Contains(member)) (refused ??= []).Add(member);
                }
            }
        }

        if (refused is null) return ids;
        return ids.Where(id => !refused.Contains(id)).ToArray();
    }

    // Only the user's locks stop a removal. Recovery protection does not: removing a protected element
    // leaves its retained file alone, so deleting it is how the user gets rid of content they cannot repair.
    private static Element[] RemovableElements(Scene scene, IReadOnlyList<Element> elements)
        => elements.Where(e => !scene.IsElementLockedByUser(e)).ToArray();

    // Ids with no matching element cannot be locked and pass through unchanged.
    private static IReadOnlyCollection<Guid> FilterUnlockedIds(Scene scene, IReadOnlyCollection<Guid> ids)
    {
        if (ids.Count == 0) return ids;

        HashSet<Guid>? locked = CollectLockedIds(scene);
        if (locked is null) return ids;
        return ids.Where(id => !locked.Contains(id)).ToArray();
    }

    // Null when nothing is locked, so callers can skip filtering without allocating a set.
    private static HashSet<Guid>? CollectLockedIds(Scene scene)
    {
        HashSet<Guid>? lockedIds = null;
        foreach (Element child in scene.Children)
        {
            if (scene.IsElementLocked(child)) (lockedIds ??= []).Add(child.Id.Value);
        }

        return lockedIds;
    }

    private static bool ShiftLocalKeyFrames(object root, MediaTime delta)
    {
        bool shifted = false;
        foreach (IKeyFrameAnimation anim in ChildObjects.Walk(root, static obj => obj is ProjectItem)
                     .OfType<IKeyFrameAnimation>()
                     .Where(anim => !anim.UseGlobalClock.Value))
        {
            foreach (IKeyFrame keyframe in anim.KeyFrames.ToArray())
            {
                keyframe.KeyTime.Value += delta;
                shifted = true;
            }
        }

        return shifted;
    }

    private static void RestoreGroupTiming(SoundGroup group)
    {
        foreach (Sound child in group.Children)
        {
            if (!child.IsTimeAnchor.Value)
            {
                child.TimeRange.Value = group.TimeRange.Value;
                child.ZIndex.Value = group.ZIndex.Value;
            }
            if (child is SoundGroup nested) RestoreGroupTiming(nested);
        }
    }

    // Group children share their clip's clock, but each source needs its own speed-aware
    // offset. Stop at scene references: their contents belong to the referenced scene.
    internal static List<ISplittable> OwnedSplittables(Element element)
    {
        var result = new List<ISplittable>();
        foreach (EngineObject obj in element.Objects) Add(obj);
        return result;

        void Add(EngineObject obj)
        {
            if (obj is ISplittable splittable) result.Add(splittable);
            if (obj is SoundGroup group)
                foreach (Sound child in group.Children) Add(child);
        }
    }

    private readonly record struct SplitSource(
        ISplittable Source, MediaTime PositionAtCut, MediaTime ContextAtCut, bool WrapVideo, bool Supported);

    private static List<SplitSource> CaptureSplitSources(Element element, MediaTime forwardDuration, SplitBatch? batch)
    {
        List<ISplittable> splittables = OwnedSplittables(element);
        var sources = new List<SplitSource>(splittables.Count);
        if (splittables.Count == 0) return sources;

        Dictionary<ISplittable, SlippableMedia.Target> targets = CollectSplitTargets(element, splittables, batch);
        foreach (ISplittable splittable in splittables)
        {
            bool wrapsVideo = splittable is SourceVideo;
            if (!targets.TryGetValue(splittable, out SlippableMedia.Target? target) || !target.IsSupported)
            {
                sources.Add(new(splittable, MediaTime.Zero, MediaTime.Zero, wrapsVideo, false));
                continue;
            }

            sources.Add(new(
                splittable,
                target.SourcePosition(forwardDuration, wrapsVideo),
                target.ContextTime(forwardDuration),
                wrapsVideo,
                true));
        }

        return sources;
    }

    private static Dictionary<ISplittable, SlippableMedia.Target> CollectSplitTargets(
        Element element, IReadOnlyList<ISplittable> splittables, SplitBatch? batch)
    {
        var targets = new Dictionary<ISplittable, SlippableMedia.Target>(ReferenceEqualityComparer.Instance);
        if (splittables.Count == 0) return targets;

        var owned = new HashSet<ISplittable>(splittables, ReferenceEqualityComparer.Instance);
        var conflicts = new HashSet<ISplittable>(ReferenceEqualityComparer.Instance);
        foreach (SlippableMedia.Target target in SlippableMedia.Collect(element, batch: batch))
        {
            if (target.Offset.GetOwnerObject() is not ISplittable owner || !owned.Contains(owner)) continue;
            if (!targets.TryAdd(owner, target))
                conflicts.Add(owner);
        }

        foreach (ISplittable owner in conflicts)
            targets.Remove(owner);
        return targets;
    }

    private static void ApplySplitSourceDeltas(
        Element backward, IReadOnlyList<SplitSource> sources, MediaTime forwardDuration, SplitBatch? batch)
    {
        List<ISplittable> copies = OwnedSplittables(backward);
        if (copies.Count != sources.Count
            || !copies.Zip(sources).All(pair => pair.First.GetType() == pair.Second.Source.GetType()))
        {
            foreach (ISplittable copy in copies)
                copy.NotifySplitted(true, forwardDuration, -forwardDuration);
            return;
        }

        Dictionary<ISplittable, SlippableMedia.Target> backTargets = CollectSplitTargets(backward, copies, batch);
        var supported = new bool[copies.Count];
        bool shifted = false;
        for (int i = 0; i < copies.Count; i++)
        {
            SplitSource source = sources[i];
            if (!source.Supported || !backTargets.TryGetValue(copies[i], out SlippableMedia.Target? backTarget))
                continue;

            MediaTime additional = backTarget.ContextTime(MediaTime.Zero) - source.ContextAtCut + forwardDuration;
            if (additional != MediaTime.Zero)
                shifted |= ShiftLocalKeyFrames(copies[i], additional);
            supported[i] = true;
        }

        if (shifted)
            backTargets = CollectSplitTargets(backward, copies, batch);

        for (int i = 0; i < copies.Count; i++)
        {
            if (!supported[i] || !backTargets.TryGetValue(copies[i], out SlippableMedia.Target? backTarget))
            {
                copies[i].NotifySplitted(true, forwardDuration, -forwardDuration);
                continue;
            }

            SplitSource source = sources[i];
            MediaTime delta = source.PositionAtCut
                              - backTarget.SourcePosition(MediaTime.Zero, source.WrapVideo);
            copies[i].NotifySplitted(true, delta, -forwardDuration);
        }
    }
}
