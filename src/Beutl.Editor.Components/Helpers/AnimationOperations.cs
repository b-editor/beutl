using System.Collections.Immutable;
using System.Reflection;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.Helpers;

public static class AnimationOperations
{
    private static readonly MethodInfo s_insertKeyFrame = typeof(AnimationOperations).GetMethods()
        .Single(m => m.Name == nameof(InsertKeyFrame) && m.IsGenericMethodDefinition);

    private static TimeSpan ConvertKeyTime(IKeyFrameAnimation animation, TimeSpan globalkeyTime, ILogger logger)
    {
        var element = animation.FindHierarchicalParent<Element>();
        var scene = animation.FindHierarchicalParent<Scene>();
        logger.LogInformation("Converting key time {GlobalKeyTime}", globalkeyTime);
        return ConvertKeyTime(animation, globalkeyTime, element, scene);
    }

    // The animation's key time at a time on the timeline, rounded to the frames of the scene.
    internal static TimeSpan ConvertKeyTime(IAnimation animation, TimeSpan globalkeyTime, Element? element, Scene? scene)
    {
        TimeSpan localKeyTime = element != null ? globalkeyTime - element.Start : globalkeyTime;
        TimeSpan keyTime = animation.UseGlobalClock ? globalkeyTime : localKeyTime;

        Project? proj = scene?.FindHierarchicalParent<Project>();
        int rate = proj?.GetFrameRate() ?? 30;

        return keyTime.RoundToRate(rate);
    }

    // InsertKeyFrame<T> for an animation whose value type is known only at run time.
    public static IKeyFrame? InsertKeyFrame(IKeyFrameAnimation animation, Easing? easing, TimeSpan keyTime, ILogger logger)
    {
        return (IKeyFrame?)s_insertKeyFrame.MakeGenericMethod(animation.ValueType)
            .Invoke(null, BindingFlags.DoNotWrapExceptions, null, [animation, easing, keyTime, logger], null);
    }

    public static KeyFrame<T>? InsertKeyFrame<T>(KeyFrameAnimation<T> animation, Easing? easing, TimeSpan keyTime, ILogger logger)
    {
        logger.LogInformation("Inserting key frame at {KeyTime}", keyTime);
        bool defaultEasing = easing is null or SplineEasing { X1: 0, Y1: 0, X2: 1, Y2: 1 };
        KeyFrame<T>? createdKeyFrame = null;
        keyTime = ConvertKeyTime(animation, keyTime, logger);

        if (animation.KeyFrames.All(x => x.KeyTime != keyTime))
        {
            (IKeyFrame? prevIKeyFrame, IKeyFrame? nextIKeyFrame) = animation.GetPreviousAndNextKeyFrame(keyTime);

            (KeyFrame<T> Next, SplineEasing Right)? split = null;
            if (defaultEasing
                && nextIKeyFrame is KeyFrame<T> nextKeyFrame
                && prevIKeyFrame is KeyFrame<T> prevKeyFrame
                && nextKeyFrame.Easing is SplineEasing existingEasing)
            {
                var duration = nextKeyFrame.KeyTime - prevKeyFrame.KeyTime;
                float t = (float)(keyTime - prevKeyFrame.KeyTime).TotalMilliseconds /
                          (float)duration.TotalMilliseconds;
                (easing, SplineEasing right) = SplineEasingHelper.SplitByT(existingEasing, t);
                split = (nextKeyFrame, right);
            }
            else
            {
                easing ??= new SplineEasing();
            }

            createdKeyFrame = new KeyFrame<T>
            {
                Value = animation.Interpolate(keyTime),
                Easing = easing,
                KeyTime = keyTime
            };

            animation.KeyFrames.AddSorted(createdKeyFrame);
            if (split is { } s)
                s.Next.Easing = s.Right;
        }

        return createdKeyFrame;
    }

    // The keyframe at keyTime, or null when there is none.
    internal static IKeyFrame? FindKeyFrame(IKeyFrameAnimation animation, TimeSpan keyTime, ILogger logger)
    {
        keyTime = ConvertKeyTime(animation, keyTime, logger);
        return animation.KeyFrames.FirstOrDefault(x => x.KeyTime == keyTime);
    }

    public static void RemoveKeyFrame(IKeyFrameAnimation animation, IKeyFrame keyframe, ILogger logger)
    {
        logger.LogInformation("Removing key frame at {KeyTime}", keyframe.KeyTime);

        var index = animation.KeyFrames.IndexOf(keyframe);

        SplineEasingHelper.Remove(animation, index);
    }
}
