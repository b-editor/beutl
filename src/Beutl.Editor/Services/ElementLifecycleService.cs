using System.Collections.Immutable;
using Beutl.Animation;
using Beutl.Language;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Utilities;

namespace Beutl.Editor.Services;

public sealed class ElementLifecycleService : IElementLifecycleService
{
    private readonly HistoryManager _historyManager;

    public ElementLifecycleService(HistoryManager historyManager)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
    }

    public void Exclude(Scene scene, IReadOnlyList<Element> elements)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(elements);
        if (elements.Count == 0) return;

        foreach (Element element in elements.ToArray())
        {
            scene.RemoveChild(element);
        }

        _historyManager.Commit(CommandNames.RemoveElement);
    }

    public void Delete(Scene scene, IReadOnlyList<Element> elements)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(elements);
        if (elements.Count == 0) return;

        foreach (Element element in elements.ToArray())
        {
            scene.DeleteChild(element);
        }

        _historyManager.Commit(CommandNames.DeleteElement);
    }

    public void SetEnabled(Element element, bool isEnabled)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.IsEnabled == isEnabled) return;

        element.IsEnabled = isEnabled;
        _historyManager.Commit(CommandNames.ChangeElementEnabled);
    }

    public void SetAccentColor(Element element, Color color)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.AccentColor == color) return;

        element.AccentColor = color;
        _historyManager.Commit(CommandNames.ChangeElementColor);
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

            // Track group membership so the resulting back-clips stay in the
            // same group as their source.
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
        if (ids.Count == 0) return GroupOutcome.NotCreated;

        // Always remove the ids from existing groups first — when only one id is
        // passed this is effectively an "ungroup this element from its set",
        // which matches the previous in-VM behavior.
        RemoveIdsFromGroups(scene, ids);

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

        _historyManager.Commit(CommandNames.GroupElements);
        return new GroupOutcome(created);
    }

    public void Ungroup(Scene scene, IReadOnlyCollection<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0) return;

        RemoveIdsFromGroups(scene, ids);
        _historyManager.Commit(CommandNames.UngroupElements);
    }

    private static void RemoveIdsFromGroups(Scene scene, IReadOnlyCollection<Guid> ids)
    {
        for (int i = scene.Groups.Count - 1; i >= 0; i--)
        {
            ImmutableHashSet<Guid> group = scene.Groups[i];
            if (!group.Overlaps(ids)) continue;

            ImmutableHashSet<Guid> updated = group.Except(ids);
            if (updated.Count >= 2)
            {
                scene.Groups[i] = updated;
            }
            else
            {
                scene.Groups.RemoveAt(i);
            }
        }
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
