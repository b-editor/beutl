using System.Collections;
using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Audio;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.Editor.Services;

[Flags]
internal enum ElementAttributeKinds
{
    Transform = 1,
    VideoEffects = 2,
    AudioEffects = 4,
    Animations = 8,
    All = Transform | VideoEffects | AudioEffects | Animations
}

internal sealed class ElementAttributePasteService(HistoryManager history)
{
    internal static Element? ReadSource(string? json)
    {
        if (json is null || ClipboardJson.TryParse(json) is not JsonObject data)
            return null;

        var source = new Element();
        CoreSerializer.PopulateFromJsonObject(source, data);
        return source.Objects.Count > 0 ? source : null;
    }

    internal static ElementAttributeKinds AvailableKinds(Scene scene, Element target)
    {
        if (!ReferenceEquals(target.HierarchicalParent, scene) || !ElementEditability.IsEditable(target, scene)) return 0;

        ElementAttributeKinds available = 0;
        foreach (EngineObject obj in target.Objects)
        {
            available |= obj switch
            {
                IFallback => 0,
                Drawable => ElementAttributeKinds.Transform | ElementAttributeKinds.VideoEffects | ElementAttributeKinds.Animations,
                Sound => ElementAttributeKinds.AudioEffects | ElementAttributeKinds.Animations,
                _ => ElementAttributeKinds.Animations
            };
        }

        return available;
    }

    internal void Paste(Scene scene, Element source, IReadOnlyList<Element> targets, ElementAttributeKinds kinds)
    {
        Element[] editable = targets.Where(target => !ReferenceEquals(target, source) && (AvailableKinds(scene, target) & kinds) != 0)
            .Distinct().ToArray();
        if (editable.Length == 0) return;

        using EditSession edit = history.BeginEdit(CommandNames.PasteAttributes);
        using (edit.Enter())
        {
            foreach (Element target in editable)
            {
                MediaTime shift = target.Start.Value - source.Start.Value;
                foreach (var (from, to) in PairObjects(source.Objects, target.Objects, ClipObjectKind))
                {
                    PasteObjectAttributes(from, to, kinds, shift);
                }
            }
        }

        edit.Complete();
    }

    private static Type ClipObjectKind(CoreObject obj) => obj switch
    {
        Drawable => typeof(Drawable),
        Sound => typeof(Sound),
        _ => obj.GetType()
    };

    private static void PasteObjectAttributes(CoreObject from, CoreObject to, ElementAttributeKinds kinds, MediaTime shift)
    {
        bool animations = kinds.HasFlag(ElementAttributeKinds.Animations);
        var replaced = new HashSet<IProperty>();
        if (from is Drawable source && to is Drawable target)
        {
            if (kinds.HasFlag(ElementAttributeKinds.Transform)
                && ReplaceValue(source.Transform, target.Transform, animations, shift))
            {
                target.TransformOrigin.Value = source.TransformOrigin.Value;
                target.AlignmentX.Value = source.AlignmentX.Value;
                target.AlignmentY.Value = source.AlignmentY.Value;
                replaced.Add(target.Transform);
            }

            if (kinds.HasFlag(ElementAttributeKinds.VideoEffects)
                && ReplaceValue(source.FilterEffect, target.FilterEffect, animations, shift))
            {
                replaced.Add(target.FilterEffect);
            }
        }
        else if (from is Sound sourceSound && to is Sound targetSound && kinds.HasFlag(ElementAttributeKinds.AudioEffects)
            && ReplaceValue(sourceSound.Effect, targetSound.Effect, animations, shift))
        {
            replaced.Add(targetSound.Effect);
        }

        if (!animations) return;

        foreach (var (sourceSlot, targetSlot) in MatchAnimations(from, to, replaced))
        {
            if (sourceSlot.Animation is not { } animation) continue;
            var copy = (IPropertyAnimation)ObjectRegeneration.Copy(animation, animation.GetType());
            if (targetSlot.Animation is { } previous && previous.GetType() == copy.GetType())
                copy.Id.Value = previous.Id.Value;
            targetSlot.SetAnimation(copy);
            ShiftAnimation(animation, copy, shift);
        }
    }

    private static bool ReplaceValue<T>(IProperty<T?> source, IProperty<T?> target, bool animations, MediaTime shift)
        where T : EngineObject
    {
        T? value = source.Value;
        if (value is IFallback) return false;
        var replacement = value is null ? null : (T)ObjectRegeneration.Copy(value, value.GetType());

        List<(IKeyFrameAnimation Track, MediaTime Origin)>? preserved = null;
        if (!animations && !PreserveAnimations(target.Value, replacement, out preserved)) return false;

        target.SetValueEvenIfEqual(replacement);
        if (preserved is not null)
        {
            foreach (var (track, origin) in preserved)
                ShiftKeys(track, origin - GetTimeOrigin(track));
        }
        if (animations && value is not null && replacement is not null)
        {
            foreach (var (from, to) in MatchAnimations(value, replacement))
            {
                if (from.Animation is { } sourceAnimation && to.Animation is { } copy)
                    ShiftAnimation(sourceAnimation, copy, shift);
            }
        }

        return true;
    }

    private static bool PreserveAnimations(CoreObject? original, CoreObject? replacement,
        out List<(IKeyFrameAnimation Track, MediaTime Origin)> preserved)
    {
        // An attribute-only paste may carry the target's tracks onto the new objects. If a track has no
        // matching property there, leave this attribute alone so the paste cannot discard that track.
        preserved = [];
        if (replacement is not null)
        {
            foreach (var (slot, _) in MatchAnimations(replacement, replacement))
            {
                if (slot.Animation is not null) slot.SetAnimation(null);
            }
        }

        if (original is null) return true;

        var remaining = MatchAnimations(original, original).Select(pair => pair.Source)
            .Where(slot => slot.Animation is not null).ToHashSet();
        if (replacement is not null && original.GetType() == replacement.GetType())
        {
            foreach (var (from, to) in MatchAnimations(original, replacement))
            {
                if (!remaining.Remove(from) || from.Animation is not { } animation) continue;

                // Keep the target's IDs, as when pasting into an existing keyframe animation.
                var copy = (IPropertyAnimation)Activator.CreateInstance(animation.GetType())!;
                CoreSerializer.PopulateFromJsonObject(copy, CoreSerializer.SerializeToJsonObject(animation));
                to.SetAnimation(copy);
                foreach (var (fromTrack, toTrack) in MatchTracks(animation, copy))
                    preserved.Add((toTrack, GetTimeOrigin(fromTrack)));
            }
        }

        return remaining.Count == 0;
    }

    private static void ShiftAnimation(IPropertyAnimation source, IPropertyAnimation target, MediaTime clipShift)
    {
        // A key stays the same distance from the clip start, even on the scene clock or on an
        // object with its own time origin. Keep its clock, value and easing as copied.
        foreach (var (from, to) in MatchTracks(source, target))
            ShiftKeys(to, clipShift + GetTimeOrigin(from) - GetTimeOrigin(to));
    }

    private static MediaTime GetTimeOrigin(IKeyFrameAnimation track)
    {
        if (track.UseGlobalClock.Value) return MediaTime.Zero;

        // A clip deserialized from the clipboard has its hierarchy but is not attached to a scene.
        // Find the authored time anchor directly, before a local clock would start following it.
        for (IHierarchical? owner = track.HierarchicalParent; owner is not null; owner = owner.HierarchicalParent)
        {
            if (owner is EngineObject { IsTimeAnchor.Value: true } engine) return engine.Start;
        }

        return MediaTime.Zero;
    }

    private static void ShiftKeys(IKeyFrameAnimation track, MediaTime delta)
    {
        if (delta == MediaTime.Zero) return;

        // Translate in time order so the sorted list does not need to move keys mid-update.
        for (int i = 0; i < track.KeyFrames.Count; i++)
        {
            IKeyFrame key = track.KeyFrames[delta < MediaTime.Zero ? i : track.KeyFrames.Count - 1 - i];
            key.KeyTime.Value += delta;
        }
    }

    private static IEnumerable<(IKeyFrameAnimation Source, IKeyFrameAnimation Target)> MatchTracks(
        IPropertyAnimation source, IPropertyAnimation target)
    {
        if (source is IKeyFrameAnimation from && target is IKeyFrameAnimation to)
        {
            yield return (from, to);
        }
        else if (source is IComponentAnimation fromComponents && target is IComponentAnimation toComponents)
        {
            foreach (var (fromPart, toPart) in fromComponents.Components.Zip(toComponents.Components))
            {
                if (fromPart.GetPropertyAnimation() is not { } original || toPart.GetPropertyAnimation() is not { } copy) continue;
                foreach (var pair in MatchTracks(original, copy)) yield return pair;
            }

            // A separated relative property can also carry a track for its unit changes.
            if (source is CoreObject fromContainer && target is CoreObject toContainer)
            {
                foreach (IProperty property in fromContainer.Properties)
                {
                    if (property.Value is IKeyFrameAnimation original
                        && toContainer.Properties.FirstOrDefault(p => p.Name == property.Name)?.Value is IKeyFrameAnimation copy)
                    {
                        yield return (original, copy);
                    }
                }
            }
        }
    }

    private static IEnumerable<(AnimationSlot Source, AnimationSlot Target)> MatchAnimations(
        CoreObject source, CoreObject target, HashSet<IProperty>? replaced = null)
    {
        if (source is IFallback || target is IFallback) yield break;

        foreach (IProperty from in source.Properties)
        {
            IProperty? to = target.Properties.FirstOrDefault(prop => prop.Name == from.Name && prop.ValueType == from.ValueType);
            if (to is null || replaced?.Contains(to) == true) continue;

            if (from is IExpressionProperty { IsAnimatable: true } && to is IExpressionProperty { IsAnimatable: true })
                yield return (new(from), new(to));

            if (!ChildObjects.MayHoldObjects(from.ValueType)
                || from is IExpressionProperty { IsReference: true } || to is IExpressionProperty { IsReference: true }) continue;

            foreach (var (a, b) in MatchValues(from.Value, to.Value))
                yield return (a, b);
        }

        if (source is INodeMember { Property: { } sourcePort } and not IEnginePropertyBackedInputPort
            && target is INodeMember { Property: { } targetPort } and not IEnginePropertyBackedInputPort
            && sourcePort.PropertyType == targetPort.PropertyType)
        {
            if (sourcePort is IAnimatablePropertyAdapter from && targetPort is IAnimatablePropertyAdapter to)
                yield return (new(from), new(to));

            foreach (var (a, b) in MatchValues(sourcePort.GetValue(), targetPort.GetValue()))
                yield return (a, b);
        }
    }

    private static IEnumerable<(AnimationSlot, AnimationSlot)> MatchValues(object? source, object? target)
    {
        source = ChildObjects.Unwrap(source);
        target = ChildObjects.Unwrap(target);
        if (source is CoreObject from && target is CoreObject to && from.GetType() == to.GetType() && from is not ProjectItem)
        {
            foreach (var pair in MatchAnimations(from, to)) yield return pair;
        }
        else if (source is IEnumerable fromItems and not string && target is IEnumerable toItems and not string)
        {
            foreach (var (a, b) in PairObjects(fromItems.OfType<CoreObject>(), toItems.OfType<CoreObject>(), obj => obj.GetType()))
            {
                if (a is ProjectItem) continue;
                foreach (var pair in MatchAnimations(a, b)) yield return pair;
            }
        }
    }

    // Repeated objects of a compatible kind are paired in their list order.
    private static IEnumerable<(CoreObject, CoreObject)> PairObjects(
        IEnumerable<CoreObject> source, IEnumerable<CoreObject> target, Func<CoreObject, Type> kind)
    {
        var targets = target.Where(obj => obj is not IFallback).ToLookup(kind);
        foreach (var group in source.Where(obj => obj is not IFallback).GroupBy(kind))
        {
            foreach (var pair in group.Zip(targets[group.Key])) yield return pair;
        }
    }

    private readonly record struct AnimationSlot(object Property)
    {
        internal IPropertyAnimation? Animation => Property is IProperty property
            ? property.GetPropertyAnimation()
            : ((IAnimatablePropertyAdapter)Property).Animation;

        internal void SetAnimation(IPropertyAnimation? animation)
        {
            if (Property is IProperty property)
                property.SetPropertyAnimation(animation);
            else
                ((IAnimatablePropertyAdapter)Property).Animation = (IKeyFrameAnimation?)animation;
        }
    }
}
