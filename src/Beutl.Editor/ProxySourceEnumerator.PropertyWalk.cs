using Beutl.Animation;
using Beutl.Audio;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Graphics3D.Textures;
using Beutl.IO;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;

namespace Beutl.Editor;

public static partial class ProxySourceEnumerator
{
    private static IEnumerable<IFileSource> EnumeratePropertyFileSources(
        EngineObject obj, TimeRange? localRange = null, bool skipDisabledElements = false, HashSet<EngineObject>? visitedValues = null,
        TimeRange? sceneWindow = null, ObjectWalkContext? walkContext = null)
    {
        visitedValues ??= new HashSet<EngineObject>(ReferenceEqualityComparer.Instance);

        foreach (IProperty property in obj.Properties)
        {
            // Connected nested inputs override this exact property, including its base, animation,
            // and object subtree. The upstream node contributes the effective sources instead.
            if (walkContext?.ConnectedNodeInputs?.Contains(property) == true) continue;
            // IProperty.GetValue evaluates an expression ahead of the animation/current value, so a
            // reference-expression pointing at a file source is what the render opens. Resolve it (no
            // evaluation — just id/path lookup) and report its sources. StringExpressions are arbitrary
            // C# and are not evaluated here.
            foreach (IFileSource source in EnumerateExpressionFileSources(obj, property.Expression, localRange, skipDisabledElements, visitedValues, sceneWindow, walkContext: walkContext))
                yield return source;

            // When an expression is present, GetValue takes the expression branch and never samples the
            // base value or the animation, so in a windowed pass (which mirrors what the render opens)
            // they must not block export. Without a window (proxy/badge scan) keep them, since an
            // unresolvable expression is not something the scanner can follow.
            bool expressionOverrides = localRange is not null && HasActiveExpression(property, walkContext);

            // A property animated by >=1 keyframe is sampled from its animation, never its base
            // CurrentValue, so when range-filtering to a render window the base must not block export —
            // its file is never opened. Without a window (proxy/badge scan) keep the base too.
            bool baseOverridden = localRange is not null && (expressionOverrides || AnimationSuppliesValue(property.Animation));
            if (!baseOverridden)
            {
                foreach (IFileSource source in EnumeratePropertyValueFileSources(property.CurrentValue, localRange, skipDisabledElements, visitedValues, walkContext))
                    yield return source;
            }

            // Rendering (and the proxy scanner) consume animated file-source values too, so media
            // referenced only from keyframes must be enumerated as well — unless an expression overrides
            // the animation entirely in a windowed pass.
            if (!expressionOverrides)
            {
                foreach (IFileSource animated in EnumerateAnimatedFileSources(property.Animation, localRange, skipDisabledElements, visitedValues, sceneWindow, walkContext))
                    yield return animated;
            }
        }

        foreach (CoreProperty prop in PropertyRegistry.GetRegistered(obj.GetType()))
        {
            if (prop.PropertyType.IsValueType)
                continue;

            foreach (IFileSource source in EnumeratePropertyValueFileSources(obj.GetValue(prop), localRange, skipDisabledElements, visitedValues, walkContext))
                yield return source;
        }
    }

    private static bool HasActiveExpression(IProperty property, ObjectWalkContext? context)
        // Discovery may run before GraphSnapshot removes a rejected connection's generated
        // expression. Its stored value/animation is already the effective fallback in that case.
        => property.Expression is { } expression
           && (expression is not INodePortExpression
               || context?.ConnectedNodeInputs?.Contains(property) != false);

    // A property value that is itself an IFileSource is the direct case; a value that is an EngineObject
    // (an ImageBrush holding ImageBrush.Source, a LutEffect holding LutEffect.Source, an
    // AudioVisualizerDrawable holding a SourceSound, …) holds rendered file sources on its own properties,
    // so recurse. Only the structural navigators with dedicated guarded walks are NOT recursed here —
    // drawables, referenced-scene sounds (SceneSound), scenes, elements, graph nodes — since re-walking
    // them through the property graph would bypass their disabled/target/window guards. A plain value
    // Sound (a visualizer's Source) has no such walk, so it must be recursed.
    private static IEnumerable<IFileSource> EnumeratePropertyValueFileSources(
        object? value, TimeRange? localRange, bool skipDisabledElements, HashSet<EngineObject> visitedValues,
        ObjectWalkContext? walkContext = null)
    {
        if (value is IFileSource fileSource)
        {
            yield return fileSource;
            yield break;
        }

        if (walkContext?.ConnectedNodeInputs != null && value is System.Collections.IList list)
        {
            foreach (EngineObject item in list.OfType<EngineObject>())
            {
                if (item is Drawable drawable)
                {
                    if (skipDisabledElements && !drawable.IsEnabled
                        || !TryVisitGraphDrawable(drawable, localRange, walkContext)) continue;
                    foreach (IFileSource source in EnumerateObjectFileSources(
                        drawable, walkContext, skipDisabledElements, localRange))
                        yield return source;
                    continue;
                }
                foreach (IFileSource source in EnumeratePropertyValueFileSources(item, localRange, skipDisabledElements, visitedValues, walkContext))
                    yield return source;
            }
            yield break;
        }

        // A DrawableBrush paints an area with a nested Drawable that BrushConstructor renders when the
        // owning shape draws, and a DrawableTextureSource (a 3D material map — DiffuseMap / AlbedoMap / …)
        // renders its nested Drawable via GetTexture, opening that drawable's files. Either drawable is
        // reachable only as a property value, so route it through the guarded drawable walk. VisitedTargets
        // stops a structurally-reached drawable being walked twice.
        if (walkContext is not null
            && ResolveNestedDrawable(value, walkContext) is { } nestedDrawable
            && (!skipDisabledElements || nestedDrawable.IsEnabled)
            && walkContext.VisitedTargets.Add(nestedDrawable))
        {
            foreach (IFileSource source in EnumerateObjectFileSources(
                nestedDrawable, walkContext, skipDisabledElements, localRange))
                yield return source;
            // Fall through so the brush's own remaining properties (Transform, …) are still walked.
        }

        // A SceneSound held as a property value (an audio visualizer's Source) contributes only its
        // referenced scene's audio; the structural `obj is SceneSound` walk never fires for a value.
        if (walkContext is { } soundContext && value is SceneSound sceneSound
            && ResolveExpressionValue<Scene>(sceneSound, sceneSound.ReferencedScene, walkContext: walkContext) is { } referencedScene)
        {
            TimeRange? soundWindow = IsIdentityAudioMap(sceneSound) ? localRange : null;
            foreach (IFileSource source in EnumerateReferencedSceneSources(
                referencedScene, soundContext.VisitedScenes, skipDisabledElements, CompositionTarget.Audio, soundWindow, soundWindow))
                yield return source;

            yield break;
        }

        // A SoundGroup held as a property value (AudioVisualizerDrawable.Source) is composed by the
        // graphics render, but the object walk's `obj is SoundGroup` branch never fires here (the
        // enumerated object is the visualizer, not the group), so recurse its child Sounds directly,
        // honouring the same IsEnabled gate the audio compose applies.
        if (value is SoundGroup soundGroup)
        {
            if (!visitedValues.Add(soundGroup))
                yield break;

            foreach (Sound child in soundGroup.Children)
            {
                if (skipDisabledElements && !child.IsEnabled)
                    continue;

                foreach (IFileSource source in EnumeratePropertyValueFileSources(child, localRange, skipDisabledElements, visitedValues, walkContext))
                    yield return source;
            }

            yield break;
        }

        if (value is not EngineObject engineObject
            || value is Drawable or SceneSound or Scene or Element or GraphNode
            || (skipDisabledElements && !engineObject.IsEnabled)
            || !visitedValues.Add(engineObject))
        {
            yield break;
        }

        foreach (IFileSource source in EnumeratePropertyFileSources(engineObject, localRange, skipDisabledElements, visitedValues, walkContext?.SceneWindow, walkContext))
            yield return source;
    }

    // The render uses the effective Drawable of a DrawableBrush / DrawableTextureSource, so resolve an
    // expression-supplied one.
    private static Drawable? ResolveNestedDrawable(object? value, ObjectWalkContext walkContext)
        => value switch
        {
            DrawableBrush brush => ResolveExpressionValue<Drawable>(brush, brush.Drawable, walkContext: walkContext),
            DrawableTextureSource textureSource
                => ResolveExpressionValue<Drawable>(textureSource, textureSource.Drawable, walkContext: walkContext),
            _ => null,
        };

    // A reference-expression resolves to another object's value (or one of its properties) by id; the
    // render opens whatever file source that yields. Resolve via the shared hierarchy root and route the
    // target through the value recursion. A cross-referenced target has its own guarded walk when it is
    // reached structurally, so recursing its resolved value here (guarded by visitedValues) only adds the
    // file sources it exposes as a value, without re-walking a scene/element/drawable navigator.
    private static IEnumerable<IFileSource> EnumerateExpressionFileSources(
        EngineObject owner, IExpression? expression, TimeRange? localRange, bool skipDisabledElements, HashSet<EngineObject> visitedValues,
        TimeRange? sceneWindow = null, HashSet<IProperty>? visitedExpressionProps = null, ObjectWalkContext? walkContext = null)
    {
        if (expression is not IReferenceExpression reference
            || owner.FindHierarchicalRoot() is not ICoreObject root
            || root.FindById(reference.ObjectId) is not EngineObject target)
        {
            yield break;
        }

        if (!reference.HasPropertyPath)
        {
            foreach (IFileSource source in EnumeratePropertyValueFileSources(target, localRange, skipDisabledElements, visitedValues, walkContext))
                yield return source;

            yield break;
        }

        // "GUID.PropertyName" resolves through PropertyLookup.TryGetPropertyValue, which tries the
        // target's IProperty surface first, then a registered CoreProperty. Mirror that resolution and,
        // for an IProperty, enumerate the same way the property walk does (its own expression, animated
        // keyframes, and current value) so the reference reaches sources hidden in the target's
        // animation/expression, not just its current value.
        IProperty? property = target.Properties.FirstOrDefault(
            p => string.Equals(p.Name, reference.PropertyPath, StringComparison.OrdinalIgnoreCase));
        if (property is not null)
        {
            if (walkContext?.ConnectedNodeInputs?.Contains(property) == true) yield break;
            // A cyclic reference chain (Target.Expression -> Target) would recurse forever; the render's
            // IsEvaluating guard breaks the cycle to DefaultValue, contributing no sources, so stop
            // descending on a re-visited property.
            visitedExpressionProps ??= new HashSet<IProperty>(ReferenceEqualityComparer.Instance);
            if (!visitedExpressionProps.Add(property))
                yield break;

            foreach (IFileSource source in EnumerateExpressionFileSources(target, property.Expression, localRange, skipDisabledElements, visitedValues, sceneWindow, visitedExpressionProps, walkContext))
                yield return source;

            bool targetExpressionOverrides = localRange is not null && HasActiveExpression(property, walkContext);
            bool targetBaseOverridden = localRange is not null && (targetExpressionOverrides || AnimationSuppliesValue(property.Animation));
            if (!targetBaseOverridden)
            {
                foreach (IFileSource source in EnumeratePropertyValueFileSources(property.CurrentValue, localRange, skipDisabledElements, visitedValues, walkContext))
                    yield return source;
            }

            if (!targetExpressionOverrides)
            {
                // The target property renders through PropertyLookup at scene time, so a global-clock
                // source animation on it is windowed by sceneWindow just like a direct property.
                foreach (IFileSource source in EnumerateAnimatedFileSources(property.Animation, localRange, skipDisabledElements, visitedValues, sceneWindow, walkContext))
                    yield return source;
            }

            yield break;
        }

        // Fall back to a registered CoreProperty (PropertyLookup's second strategy). CoreProperties are
        // not animatable/expressible on this surface, so only the resolved value matters.
        CoreProperty? coreProperty = PropertyRegistry.GetRegistered(target.GetType())
            .FirstOrDefault(p => string.Equals(p.Name, reference.PropertyPath, StringComparison.OrdinalIgnoreCase));
        if (coreProperty is not null && !coreProperty.PropertyType.IsValueType)
        {
            foreach (IFileSource source in EnumeratePropertyValueFileSources(target.GetValue(coreProperty), localRange, skipDisabledElements, visitedValues, walkContext))
                yield return source;
        }
    }

    // Conservative: treat the base CurrentValue as in-play unless every keyframe is non-null.
    // KeyFrameAnimation.Interpolate returns the non-null neighbour when only one side of a pair is
    // null, so a lone null keyframe does not always make GetAnimatedValue return null — but it CAN
    // (a trailing/sole null keyframe, or a span of consecutive nulls), and pinpointing exactly when
    // would risk dropping a base the render falls back to (GetAnimatedValue(...) ?? CurrentValue).
    // Over-reporting the base is the safe direction: it never omits a file the render opens.
    private static bool AnimationSuppliesValue(IAnimation? animation)
        => animation is KeyFrameAnimation { KeyFrames.Count: > 0 } keyFrameAnimation
           && keyFrameAnimation.KeyFrames.All(k => k.Value is not null);

    // GetValue evaluates a reference-expression ahead of the base value and never samples CurrentValue
    // while an expression is set, so resolve the reference (id/path lookup, no evaluation) and mirror the
    // evaluator: an unresolvable reference yields DefaultValue, never the stale base. Only a non-reference
    // expression (a StringExpression, arbitrary C#) — which this walk cannot evaluate — best-efforts to
    // CurrentValue.
    private static T? ResolveExpressionValue<T>(EngineObject owner, IProperty property, HashSet<IProperty>? visited = null,
        ObjectWalkContext? walkContext = null)
        where T : class
    {
        if (walkContext?.ConnectedNodeInputs?.Contains(property) == true) return null;
        // A user-constructed reference chain (Target.Expression -> Target) can cycle; the engine's own
        // evaluation is cycle-guarded by ExpressionContext, breaking the cycle to DefaultValue (null for
        // a reference type). This reference walk is outside that guard, so track visited properties and
        // return DefaultValue on re-entry — never CurrentValue, which the render never opens.
        visited ??= new HashSet<IProperty>(ReferenceEqualityComparer.Instance);
        if (!visited.Add(property))
            return property.DefaultValue as T;

        if (property.Expression is IReferenceExpression reference)
        {
            if (owner.FindHierarchicalRoot() is ICoreObject root
                && root.FindById(reference.ObjectId) is { } resolved)
            {
                if (!reference.HasPropertyPath)
                    return resolved as T ?? property.DefaultValue as T;

                if (resolved is EngineObject target)
                {
                    if (target.Properties.FirstOrDefault(
                            p => string.Equals(p.Name, reference.PropertyPath, StringComparison.OrdinalIgnoreCase)) is { } targetProperty)
                    {
                        return ResolveExpressionValue<T>(target, targetProperty, visited, walkContext);
                    }

                    // PropertyLookup's second strategy: a registered CoreProperty (e.g. a plugin exposing
                    // CoreProperty<Drawable>). It is not animatable/expressible on this surface, so the
                    // resolved value is what the render opens — mirror EnumerateExpressionFileSources.
                    CoreProperty? coreProperty = PropertyRegistry.GetRegistered(target.GetType())
                        .FirstOrDefault(p => string.Equals(p.Name, reference.PropertyPath, StringComparison.OrdinalIgnoreCase));
                    if (coreProperty is not null && !coreProperty.PropertyType.IsValueType)
                        return target.GetValue(coreProperty) as T ?? property.DefaultValue as T;
                }
            }

            // Reference present but unresolvable (missing id/path, or a non-EngineObject on the path):
            // the evaluator returns DefaultValue, so a stale CurrentValue must not leak into preflight.
            return property.DefaultValue as T;
        }

        return property.CurrentValue as T;
    }

    private static IEnumerable<IFileSource> EnumerateAnimatedFileSources(
        IAnimation? animation, TimeRange? localRange = null, bool skipDisabledElements = false, HashSet<EngineObject>? visitedValues = null,
        TimeRange? sceneWindow = null, ObjectWalkContext? walkContext = null)
    {
        if (animation is not KeyFrameAnimation keyFrameAnimation)
            yield break;

        visitedValues ??= new HashSet<EngineObject>(ReferenceEqualityComparer.Instance);

        // Global-clock keyframes are sampled at scene time, element-local keyframes at localRange. Pick
        // the matching window; a null sceneWindow (descent crossed a time remap, so scene time is no
        // longer known) falls back to the broad walk for global-clock keyframes.
        TimeRange? window = keyFrameAnimation.UseGlobalClock ? sceneWindow : localRange;

        IReadOnlyList<IKeyFrame> keyFrames = keyFrameAnimation.KeyFrames;
        for (int i = 0; i < keyFrames.Count; i++)
        {
            // A keyframe value is either a direct IFileSource or an EngineObject holding nested sources
            // (an animated Fill switching to an ImageBrush, an animated LutEffect, …); a null value
            // contributes nothing (the render falls back to the base, handled by AnimationSuppliesValue).
            if (keyFrames[i].Value is not { } value)
                continue;

            // With no render window every keyframe value counts (proxy scan / badge enumeration). With
            // one, keep a keyframe only if a sample inside the window could resolve to it. For an object
            // value (IFileSource) KeyFrameAnimation.Interpolate returns the NEXT keyframe's value, and
            // GetPreviousAndNextKeyFrame picks a key as `next` when `prev.KeyTime < t <= this.KeyTime`, so
            // keyframe i is the sampled value on the left-open span (previous key time, this key time] —
            // the last one holds forward to +inf. A zero-duration window is a point sample {Start} (kept
            // iff the point lies in the span), NOT an empty half-open range; a non-empty window [Start,End)
            // keeps the span iff (spanStart, spanEnd] and [Start, End) overlap.
            if (window is { } range)
            {
                TimeSpan spanStart = i > 0 ? keyFrames[i - 1].KeyTime : TimeSpan.MinValue;
                TimeSpan spanEnd = i < keyFrames.Count - 1 ? keyFrames[i].KeyTime : TimeSpan.MaxValue;
                bool keep = range.Duration <= TimeSpan.Zero
                    ? spanStart < range.Start && range.Start <= spanEnd
                    : spanStart < range.End && range.Start <= spanEnd;
                if (!keep)
                    continue;
            }

            foreach (IFileSource source in EnumeratePropertyValueFileSources(value, localRange, skipDisabledElements, visitedValues, walkContext))
                yield return source;
        }
    }
}
