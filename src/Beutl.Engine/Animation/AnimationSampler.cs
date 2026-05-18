using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Animation;

public sealed class AnimationSampler
{
    public void SampleBuffer<T>(
        IProperty<T> property,
        TimeRange range,
        int sampleRate,
        Span<T> output
    )
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(property);

        var animation = property.Animation;
        if (animation is KeyFrameAnimation<T> keyFrameAnimation)
        {
            // 音声グラフでは ClipNode で時刻が要素ローカルに変換されているため、
            // GetAnimatedValue が期待するグローバル時刻へ戻すために owner.TimeRange.Start を加算する。
            // (UseGlobalClock=false なら GetAnimatedValue が再度引き、結果としてローカルで補間される)
            var ownerStart = property.GetOwnerObject()?.TimeRange.Start ?? TimeSpan.Zero;
            for (int i = 0; i < output.Length; i++)
            {
                output[i] = keyFrameAnimation.GetAnimatedValue(
                    ownerStart + range.Start + TimeSpan.FromSeconds(i / (double)sampleRate)
                );
            }
        }
        else
        {
            output.Fill(property.CurrentValue);
        }
    }
}
