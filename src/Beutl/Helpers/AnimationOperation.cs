using System.Collections.Immutable;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;

namespace Beutl.Helpers;

public static class AnimationOperations
{
    private static TimeSpan ConvertKeyTime(IKeyFrameAnimation animation, TimeSpan globalkeyTime, ILogger logger)
    {
        var element = animation.FindHierarchicalParent<Element>();
        var scene = animation.FindHierarchicalParent<Scene>();
        logger.LogInformation("Converting key time {GlobalKeyTime}", globalkeyTime);
        TimeSpan localKeyTime = element != null ? globalkeyTime - element.Start : globalkeyTime;
        TimeSpan keyTime = animation.UseGlobalClock ? globalkeyTime : localKeyTime;

        Project? proj = scene?.FindHierarchicalParent<Project>();
        int rate = proj?.GetFrameRate() ?? 30;

        return keyTime.RoundToRate(rate);
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

            if (defaultEasing
                && nextIKeyFrame is KeyFrame<T> nextKeyFrame
                && prevIKeyFrame is KeyFrame<T> prevKeyFrame
                && nextKeyFrame.Easing is SplineEasing existingEasing)
            {
                var duration = nextKeyFrame.KeyTime - prevKeyFrame.KeyTime;
                float t = (float)(keyTime - prevKeyFrame.KeyTime).TotalMilliseconds /
                          (float)duration.TotalMilliseconds;
                var (left, right) = SplineEasingHelper.SplitByT(existingEasing, t);
                createdKeyFrame = new KeyFrame<T>
                {
                    Value = animation.Interpolate(keyTime),
                    Easing = left,
                    KeyTime = keyTime
                };

                animation.KeyFrames.Add(createdKeyFrame, out _);
                nextKeyFrame.Easing = right;
            }
            else
            {
                easing ??= new SplineEasing();
                createdKeyFrame = new KeyFrame<T>
                {
                    Value = animation.Interpolate(keyTime),
                    Easing = easing,
                    KeyTime = keyTime
                };

                animation.KeyFrames.Add(createdKeyFrame, out _);
            }
        }

        return createdKeyFrame;
    }

    public static void RemoveKeyFrame(IKeyFrameAnimation animation, TimeSpan keyTime, ILogger logger)
    {
        logger.LogInformation("Removing key frame at {KeyTime}", keyTime);
        keyTime = ConvertKeyTime(animation, keyTime, logger);
        IKeyFrame? keyframe = animation.KeyFrames.FirstOrDefault(x => x.KeyTime == keyTime);
        if (keyframe == null) return;

        var index = animation.KeyFrames.IndexOf(keyframe);

        SplineEasingHelper.Remove(animation, index);
    }

    public static void RemoveKeyFrame(IKeyFrameAnimation animation, IKeyFrame keyframe, ILogger logger)
    {
        logger.LogInformation("Removing key frame at {KeyTime}", keyframe.KeyTime);

        var index = animation.KeyFrames.IndexOf(keyframe);

        // 次の要素があるとき
        SplineEasingHelper.Remove(animation, index);
    }
}
