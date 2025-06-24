using System;
using Beutl.Animation;
using Beutl.Media;

namespace Beutl.Audio.Graph;

public interface IAnimationSampler
{
    T Sample<T>(IAnimatable target, CoreProperty<T> property, TimeSpan time)
        where T : notnull;
    
    void SampleBuffer<T>(
        IAnimatable target, 
        CoreProperty<T> property, 
        TimeRange range, 
        int sampleCount,
        Span<T> output)
        where T : struct;
}