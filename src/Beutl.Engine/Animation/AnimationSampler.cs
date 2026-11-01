using System.Runtime.CompilerServices;
using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Animation;

public sealed class AnimationSampler
{
    // An expression runs a script, far too slow to evaluate for every sample, so it is evaluated on a grid of this
    // many flicks, 100 times a second whatever the sample rate, and interpolated in between.
    private const long ExpressionInterval = MediaTime.FlicksPerSecond / 100;

    // Whether property changes over time, through keyframes or an expression, so that a node samples it.
    internal static bool IsAnimated(IProperty property)
        => property is IExpressionProperty { HasExpression: true } || property.GetKeyFrameAnimation() is not null;

    public void SampleBuffer<T>(
        IProperty<T> property,
        TimeRange range,
        int sampleRate,
        Span<T> output)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(property);

        if (property is IExpressionProperty<T> { HasExpression: true } expressionProperty)
        {
            // An expression is evaluated at the global time, as keyframes are below.
            SampleExpression(expressionProperty, GetOwnerStart(property) + range.Start, sampleRate, output);
            return;
        }

        var animation = property.GetPropertyAnimation();
        if (animation is IPropertyAnimation<T> keyFrameAnimation)
        {
            // 音声グラフでは ClipNode で時刻が要素ローカルに変換されているため、
            // GetAnimatedValue が期待するグローバル時刻へ戻すために owner.TimeRange.Start を加算する。
            // (UseGlobalClock=false なら GetAnimatedValue が再度引き、結果としてローカルで補間される)
            var ownerStart = GetOwnerStart(property);
            for (int i = 0; i < output.Length; i++)
            {
                output[i] = keyFrameAnimation.EvaluateAt(
                    ownerStart + range.Start + MediaTime.FromSeconds(i / (double)sampleRate));
            }
        }
        else
        {
            output.Fill(property.Value);
        }
    }

    private static MediaTime GetOwnerStart(IProperty property)
        => (property.GetOwnerObject() as EngineObject)?.TimeRange.Value.Start ?? MediaTime.Zero;

    private static void SampleExpression<T>(
        IExpressionProperty<T> property, MediaTime start, int sampleRate, Span<T> output)
        where T : struct
    {
        double flicksPerSample = (double)MediaTime.FlicksPerSecond / sampleRate;
        long cell = long.MinValue;
        T from = default;
        T to = default;
        for (int i = 0; i < output.Length; i++)
        {
            double time = start.Flicks + i * flicksPerSample;
            long index = (long)Math.Floor(time / ExpressionInterval);
            if (index != cell)
            {
                from = index == cell + 1 ? to : Evaluate(property, index);
                to = Evaluate(property, index + 1);
                cell = index;
            }

            output[i] = Interpolate(from, to, (time - (double)index * ExpressionInterval) / ExpressionInterval);
        }
    }

    private static T Evaluate<T>(IExpressionProperty<T> property, long index)
        => property.Evaluate(new EvaluationContext(new MediaTime(index * ExpressionInterval)));

    // Linear for a float, which is what the audio graph samples; any other value holds until the next grid point.
    private static T Interpolate<T>(T from, T to, double fraction)
        where T : struct
    {
        if (typeof(T) != typeof(float))
            return from;

        float a = Unsafe.As<T, float>(ref from);
        float b = Unsafe.As<T, float>(ref to);
        float value = (float)(a + (b - a) * fraction);
        return Unsafe.As<float, T>(ref value);
    }
}
