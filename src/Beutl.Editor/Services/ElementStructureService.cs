using System.Collections.Immutable;
using Beutl.Animation;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Utilities;

namespace Beutl.Editor.Services;

public sealed class ElementStructureService : IElementStructureService
{
    private readonly HistoryManager _historyManager;

    public ElementStructureService(HistoryManager historyManager)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
    }

    public void Exclude(Scene scene, IReadOnlyList<Element> elements, bool ripple = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(elements);
        Element[] editable = elements.Where(e => !scene.IsElementLocked(e)).ToArray();
        if (editable.Length == 0) return;

        RippleHelper.RemoveAndShiftAfter(scene, editable, ripple, scene.RemoveChild);

        RemoveIdsFromGroups(scene, editable.Select(e => e.Id).ToArray());

        _historyManager.Commit(CommandNames.RemoveElement);
    }

    public void Delete(Scene scene, IReadOnlyList<Element> elements, bool ripple = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(elements);
        Element[] editable = elements.Where(e => !scene.IsElementLocked(e)).ToArray();
        if (editable.Length == 0) return;

        RippleHelper.RemoveAndShiftAfter(scene, editable, ripple, scene.DeleteChild);

        RemoveIdsFromGroups(scene, editable.Select(e => e.Id).ToArray());

        _historyManager.Commit(CommandNames.DeleteElement);
    }

    public SplitOutcome Split(Scene scene, IReadOnlyList<Element> targets, TimeSpan at)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0 || scene.Uri is null) return SplitOutcome.Empty;

        int rate = SceneTimeRangeService.GetFrameRate(scene);
        TimeSpan minDuration = TimeSpan.FromSeconds(1d / rate);
        var newElements = new List<Element>();
        var groupUpdates = new Dictionary<int, List<Guid>>();

        foreach (Element target in targets)
        {
            if (scene.IsElementLocked(target)) continue;

            TimeSpan forwardDuration = at - target.Start;
            TimeSpan backwardDuration = target.Length - forwardDuration;
            if (forwardDuration < minDuration || backwardDuration < minDuration) continue;

            ObjectRegenerator.Regenerate(target, out Element backward);

            scene.MoveChild(target.ZIndex, target.Start, forwardDuration, target);
            backward.Start = at;
            backward.Length = backwardDuration;

            ShiftLocalKeyFrames(backward, -forwardDuration);

            CoreSerializer.StoreToUri(backward, RandomFileNameGenerator.GenerateUri(scene.Uri, EditorConstants.ElementFileExtension));
            scene.AddChild(backward);
            backward.NotifySplitted(true, forwardDuration, -forwardDuration);
            target.NotifySplitted(false, TimeSpan.Zero, -backwardDuration);

            newElements.Add(backward);

            // Track group membership so back-clips stay in their source's group.
            int groupIndex = -1;
            for (int i = 0; i < scene.Groups.Count; i++)
            {
                if (scene.Groups[i].Contains(target.Id))
                {
                    groupIndex = i;
                    break;
                }
            }

            if (groupIndex >= 0)
            {
                if (!groupUpdates.TryGetValue(groupIndex, out List<Guid>? newIds))
                {
                    newIds = [];
                    groupUpdates.Add(groupIndex, newIds);
                }

                newIds.Add(backward.Id);
            }
        }

        if (newElements.Count == 0) return SplitOutcome.Empty;

        foreach ((int index, List<Guid> value) in groupUpdates.OrderByDescending(x => x.Key))
        {
            ImmutableHashSet<Guid> newGroup = [.. value];
            if (newGroup.Count >= 2)
            {
                scene.Groups.Insert(index + 1, newGroup);
            }
        }

        _historyManager.Commit(CommandNames.SplitElement);
        return new SplitOutcome(newElements);
    }

    public GroupOutcome Group(Scene scene, IReadOnlyCollection<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(ids);
        ids = FilterStrandingIds(scene, FilterUnlockedIds(scene, ids));
        if (ids.Count == 0) return GroupOutcome.NotCreated;

        // Remove ids from existing groups first — with a single id this acts as
        // "ungroup this element", matching the previous in-VM behavior.
        bool removed = RemoveIdsFromGroups(scene, ids);

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

        return new GroupOutcome(created);
    }

    public void Ungroup(Scene scene, IReadOnlyCollection<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(ids);
        ids = FilterStrandingIds(scene, FilterUnlockedIds(scene, ids));
        if (ids.Count == 0) return;

        if (RemoveIdsFromGroups(scene, ids))
        {
            _historyManager.Commit(CommandNames.UngroupElements);
        }
    }

    private static bool RemoveIdsFromGroups(Scene scene, IReadOnlyCollection<Guid> ids)
        => scene.RemoveElementsFromGroups(ids);

    // Refuses to pull an editable member out of a group when that would leave the group with only a
    // locked survivor: disbanding it would silently change the locked member's grouping. Such ids are
    // dropped from the request so the group is left intact. Only structural regroups (Ungroup/Group)
    // use this — removal ops (Delete/Exclude/Cut) must always prune a removed element's id.
    private static IReadOnlyCollection<Guid> FilterStrandingIds(Scene scene, IReadOnlyCollection<Guid> ids)
    {
        if (ids.Count == 0) return ids;

        HashSet<Guid>? lockedIds = null;
        foreach (Element child in scene.Children)
        {
            if (scene.IsElementLocked(child)) (lockedIds ??= []).Add(child.Id);
        }

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

    // Ids with no matching element cannot be locked and pass through unchanged.
    private static IReadOnlyCollection<Guid> FilterUnlockedIds(Scene scene, IReadOnlyCollection<Guid> ids)
    {
        if (ids.Count == 0) return ids;

        HashSet<Guid>? locked = null;
        foreach (Element child in scene.Children)
        {
            if (scene.IsElementLocked(child))
            {
                (locked ??= []).Add(child.Id);
            }
        }

        if (locked is null) return ids;
        return ids.Where(id => !locked.Contains(id)).ToArray();
    }

    private static void ShiftLocalKeyFrames(Element element, TimeSpan delta)
    {
        foreach (KeyFrameAnimation anim in new ObjectSearcher(element, o => o is KeyFrameAnimation { UseGlobalClock: false })
                     .SearchAll()
                     .OfType<KeyFrameAnimation>())
        {
            foreach (IKeyFrame keyframe in anim.KeyFrames)
            {
                keyframe.KeyTime += delta;
            }
        }
    }
}
