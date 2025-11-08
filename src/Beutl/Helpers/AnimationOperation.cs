using System.Collections.Immutable;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;

namespace Beutl.Helpers;

public class AnimationOperations
{
    private static TimeSpan ConvertKeyTime(IKeyFrameAnimation animation, Scene? scene, Element? element,
        TimeSpan globalkeyTime, ILogger logger)
    {
        logger.LogInformation("Converting key time {GlobalKeyTime}", globalkeyTime);
        TimeSpan localKeyTime = element != null ? globalkeyTime - element.Start : globalkeyTime;
        TimeSpan keyTime = animation.UseGlobalClock ? globalkeyTime : localKeyTime;

        Project? proj = scene?.FindHierarchicalParent<Project>();
        int rate = proj?.GetFrameRate() ?? 30;

        return keyTime.RoundToRate(rate);
    }

    public static KeyFrame<T>? InsertKeyFrame<T>(KeyFrameAnimation<T> animation, Scene? scene, Element? element,
        Easing? easing, TimeSpan keyTime, ILogger logger, CommandRecorder cr, ImmutableArray<IStorable?> storables)
    {
        logger.LogInformation("Inserting key frame at {KeyTime}", keyTime);
        bool defaultEasing = easing is null or SplineEasing { X1: 0, Y1: 0, X2: 1, Y2: 1 };
        KeyFrame<T>? createdKeyFrame = null;
        keyTime = ConvertKeyTime(animation, scene, element, keyTime, logger);

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
                    Value = animation.Interpolate(keyTime), Easing = left, KeyTime = keyTime
                };

                var oldNextEasing = nextKeyFrame.Easing;
                RecordableCommands.Create(storables)
                    .OnDo(() =>
                    {
                        animation.KeyFrames.Add(createdKeyFrame, out _);
                        nextKeyFrame.Easing = right;
                    })
                    .OnUndo(() =>
                    {
                        nextKeyFrame.Easing = oldNextEasing;
                        animation.KeyFrames.Remove(createdKeyFrame);
                    })
                    .ToCommand()
                    .DoAndRecord(cr);
            }
            else
            {
                easing ??= new SplineEasing();
                createdKeyFrame = new KeyFrame<T>
                {
                    Value = animation.Interpolate(keyTime), Easing = easing, KeyTime = keyTime
                };

                RecordableCommands.Create(storables)
                    .OnDo(() => animation.KeyFrames.Add(createdKeyFrame, out _))
                    .OnUndo(() => animation.KeyFrames.Remove(createdKeyFrame))
                    .ToCommand()
                    .DoAndRecord(cr);
            }
        }

        return createdKeyFrame;
    }

    public static void RemoveKeyFrame(IKeyFrameAnimation animation, Scene? scene, Element? element,
        TimeSpan keyTime, ILogger logger, CommandRecorder cr, ImmutableArray<IStorable?> storables)
    {
        logger.LogInformation("Removing key frame at {KeyTime}", keyTime);
        keyTime = ConvertKeyTime(animation, scene, element, keyTime, logger);
        IKeyFrame? keyframe = animation.KeyFrames.FirstOrDefault(x => x.KeyTime == keyTime);
        if (keyframe == null) return;

        var index = animation.KeyFrames.IndexOf(keyframe);

        // 次の要素があるとき
        var oldEasing = keyframe.Easing;
        RecordableCommands.Create(storables)
            .OnDo(() => SplineEasingHelper.Remove(animation, index))
            .OnUndo(() =>
            {
                keyframe.Easing = oldEasing;
                animation.KeyFrames.Add(keyframe, out _);
            })
            .ToCommand()
            .DoAndRecord(cr);
    }

    public static void RemoveKeyFrame(IKeyFrameAnimation animation, Scene? scene, Element? element,
        IKeyFrame keyframe, ILogger logger, CommandRecorder cr, ImmutableArray<IStorable?> storables)
    {
        logger.LogInformation("Removing key frame at {KeyTime}", keyframe.KeyTime);

        var index = animation.KeyFrames.IndexOf(keyframe);

        // 次の要素があるとき
        var oldEasing = keyframe.Easing;
        RecordableCommands.Create(storables)
            .OnDo(() => SplineEasingHelper.Remove(animation, index))
            .OnUndo(() =>
            {
                keyframe.Easing = oldEasing;
                animation.KeyFrames.Add(keyframe, out _);
            })
            .ToCommand()
            .DoAndRecord(cr);
    }
}
