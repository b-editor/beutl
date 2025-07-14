using Beutl.Audio.Graph;
using Beutl.Media;

namespace Beutl.Animation;

public sealed class AnimationSampler
{
    public T Sample<T>(IAnimatable target, CoreProperty<T> property, TimeSpan time, int sampleRate)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);

        var animation = target.Animations.FirstOrDefault(i => i.Property.Id == property.Id);
        if (animation is KeyFrameAnimation<T> keyFrameAnimation)
        {
            return keyFrameAnimation.Interpolate(time)!;
        }

        if (target is ICoreObject coreObject)
        {
            return coreObject.GetValue(property);
        }

        throw new InvalidOperationException($"Target must implement ICoreObject to get property values. Type: {target.GetType()}");
    }

    public void SampleBuffer<T>(
        IAnimatable target,
        CoreProperty<T> property,
        TimeRange range,
        int sampleRate,
        Span<T> output)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);

        var animation = target.Animations.FirstOrDefault(i => i.Property.Id == property.Id);
        if (animation is KeyFrameAnimation<T> keyFrameAnimation)
        {
            for (int i = 0; i < output.Length; i++)
            {
                output[i] = keyFrameAnimation.Interpolate(range.Start + TimeSpan.FromSeconds(i / (double)sampleRate));
            }
        }
        else if(target is ICoreObject coreObject)
        {
            output.Fill(coreObject.GetValue(property));
        }
    }

    public bool IsAnimated(IAnimatable? target, CoreProperty? property)
    {
        if(target is null || property is null)
        {
            return false;
        }

        return target.Animations.Any(i => i.Property.Id == property.Id);
    }
}
