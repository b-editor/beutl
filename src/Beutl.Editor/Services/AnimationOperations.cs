using System.Collections.Immutable;
using System.Reactive.Linq;
using System.Reflection;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Engine;
using Beutl.JsonConverters;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Reactive;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Services;

internal static class AnimationOperations
{
    private static readonly MethodInfo s_insertKeyFrame = typeof(AnimationOperations).GetMethods()
        .Single(m => m.Name == nameof(InsertKeyFrame) && m.IsGenericMethodDefinition);

    private static MediaTime ConvertKeyTime(IKeyFrameAnimation animation, MediaTime globalkeyTime, ILogger logger)
    {
        var scene = animation.FindHierarchicalParent<Scene>();
        logger.LogInformation("Converting key time {GlobalKeyTime}", globalkeyTime);
        return ConvertKeyTime(animation, globalkeyTime, scene);
    }

    // The animation's key time at a time on the timeline, rounded to the frames of the scene.
    internal static MediaTime ConvertKeyTime(IKeyFrameAnimation animation, MediaTime globalkeyTime, Scene? scene)
        => animation.ToLocal(globalkeyTime).RoundToFrame(GetFrameRate(scene), MidpointRounding.AwayFromZero);

    internal static MediaTime GetTimeOrigin(IKeyFrameAnimation animation, EngineObject? fallbackOwner = null)
        => animation.HierarchicalParent == null && !animation.UseGlobalClock.Value
            ? fallbackOwner?.Start ?? MediaTime.Zero
            : animation.ToScene(MediaTime.Zero);

    internal static IObservable<MediaTime> ObserveTimeOrigin(IKeyFrameAnimation animation, EngineObject? fallbackOwner = null)
        => Observable.FromEventPattern<HierarchyAttachmentEventArgs>(
                h => animation.AttachedToHierarchy += h, h => animation.AttachedToHierarchy -= h)
            .Merge(Observable.FromEventPattern<HierarchyAttachmentEventArgs>(
                h => animation.DetachedFromHierarchy += h, h => animation.DetachedFromHierarchy -= h))
            .Select(_ => 0)
            .StartWith(0)
            .Select(_ => (animation.FindHierarchicalParent<EngineObject>() ?? fallbackOwner)?.TimeRange.GetObservable()
                ?? Observable.ReturnThenNever(default(TimeRange)))
            .Switch()
            .CombineLatest(animation.UseGlobalClock.GetObservable())
            .Select(_ => GetTimeOrigin(animation, fallbackOwner))
            .DistinctUntilChanged();

    // The frame rate key times snap to: the project's, or 30 fps outside a project.
    internal static Rational GetFrameRate(Scene? scene) => scene?.FindHierarchicalParent<Project>().GetFrameRate() ?? new Rational(30);

    // The keyframe at keyTime, where a key time within a tick of a frame, as a template's can be, is at that frame.
    internal static IKeyFrame? FindKeyFrameOnFrame(IEnumerable<IKeyFrame> keyFrames, MediaTime keyTime, Rational rate)
    {
        MediaTime at = MediaTimeJsonConverter.SnapToFrame(keyTime, rate);
        return keyFrames.FirstOrDefault(k => MediaTimeJsonConverter.SnapToFrame(k.KeyTime.Value, rate) == at);
    }

    // InsertKeyFrame<T> for an animation whose value type is known only at run time.
    public static IKeyFrame? InsertKeyFrame(IKeyFrameAnimation animation, Easing? easing, MediaTime keyTime, ILogger logger)
    {
        return (IKeyFrame?)s_insertKeyFrame.MakeGenericMethod(animation.ValueType)
            .Invoke(null, BindingFlags.DoNotWrapExceptions, null, [animation, easing, keyTime, logger], null);
    }

    public static KeyFrame<T>? InsertKeyFrame<T>(KeyFrameAnimation<T> animation, Easing? easing, MediaTime keyTime, ILogger logger)
    {
        logger.LogInformation("Inserting key frame at {KeyTime}", keyTime);
        bool defaultEasing = easing is null or SplineEasing { X1: 0, Y1: 0, X2: 1, Y2: 1 };
        KeyFrame<T>? createdKeyFrame = null;
        keyTime = ConvertKeyTime(animation, keyTime, logger);

        if (FindKeyFrameOnFrame(animation.KeyFrames, keyTime, GetFrameRate(animation.FindHierarchicalParent<Scene>())) == null)
        {
            (IKeyFrame? prevIKeyFrame, IKeyFrame? nextIKeyFrame) = animation.GetPreviousAndNextKeyFrame(keyTime);

            (KeyFrame<T> Next, SplineEasing Right)? split = null;
            if (defaultEasing
                && nextIKeyFrame is KeyFrame<T> nextKeyFrame
                && prevIKeyFrame is KeyFrame<T> prevKeyFrame
                && nextKeyFrame.Easing.Value is SplineEasing existingEasing)
            {
                var duration = nextKeyFrame.KeyTime.Value - prevKeyFrame.KeyTime.Value;
                float t = (float)((double)(keyTime - prevKeyFrame.KeyTime.Value).Flicks / duration.Flicks);
                (easing, SplineEasing right) = SplineEasingHelper.SplitByT(existingEasing, t);
                split = (nextKeyFrame, right);
            }
            else
            {
                easing ??= new SplineEasing();
            }

            createdKeyFrame = new KeyFrame<T>
            {
                Value = { Value = animation.Interpolate(keyTime) },
                Easing = { Value = easing },
                KeyTime = { Value = keyTime }
            };

            animation.KeyFrames.Add(createdKeyFrame, out _);
            if (split is { } s)
                s.Next.Easing.Value = s.Right;
        }

        return createdKeyFrame;
    }

    // The keyframe at keyTime, or null when there is none.
    internal static IKeyFrame? FindKeyFrame(IKeyFrameAnimation animation, MediaTime keyTime, ILogger logger)
    {
        keyTime = ConvertKeyTime(animation, keyTime, logger);
        return FindKeyFrameOnFrame(animation.KeyFrames, keyTime, GetFrameRate(animation.FindHierarchicalParent<Scene>()));
    }

    public static void RemoveKeyFrame(IKeyFrameAnimation animation, IKeyFrame keyframe, ILogger logger)
    {
        logger.LogInformation("Removing key frame at {KeyTime}", keyframe.KeyTime.Value);

        var index = animation.KeyFrames.IndexOf(keyframe);

        SplineEasingHelper.Remove(animation, index);
    }
}
