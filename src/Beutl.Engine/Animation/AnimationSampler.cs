using Beutl.Engine;
using Beutl.Media;

namespace Beutl.Animation;

public sealed class AnimationSampler
{
    public void SampleBuffer<T>(
        IProperty<T> property,
        TimeRange range,
        int sampleRate,
        Span<T> output)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(property);

        var animation = property.Animation;
        if (animation is KeyFrameAnimation<T> keyFrameAnimation)
        {
            for (int i = 0; i < output.Length; i++)
            {
                output[i] = keyFrameAnimation.Interpolate(range.Start + TimeSpan.FromSeconds(i / (double)sampleRate));
            }
        }
        else
        {
            output.Fill(property.CurrentValue);
        }
    }
}
