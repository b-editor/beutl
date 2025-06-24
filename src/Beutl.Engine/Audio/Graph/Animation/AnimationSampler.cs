using System;
using System.Collections.Generic;
using Beutl.Animation;
using Beutl.Media;

namespace Beutl.Audio.Graph.Animation;

public sealed class AnimationSampler : IAnimationSampler
{
    private readonly Dictionary<(IAnimatable, CoreProperty), CompiledAnimation> _compiledAnimations = new();
    private TimeRange? _currentRange;
    private int _currentSampleRate;

    public void PrepareAnimations(IAnimatable target, TimeRange range, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(target);
        
        _currentRange = range;
        _currentSampleRate = sampleRate;
        
        // Clear previously compiled animations
        _compiledAnimations.Clear();
        
        // Compile animations for this target
        foreach (var animation in target.Animations)
        {
            var key = (target, animation.Property);
            var compiled = new CompiledAnimation(animation, range, sampleRate);
            _compiledAnimations[key] = compiled;
        }
    }

    public T Sample<T>(IAnimatable target, CoreProperty<T> property, TimeSpan time)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);

        var key = (target, (CoreProperty)property);
        
        if (_compiledAnimations.TryGetValue(key, out var compiled))
        {
            return (T)compiled.Sample(time);
        }
        
        // No animation found, return current value
        return target.GetValue(property);
    }

    public void SampleBuffer<T>(
        IAnimatable target, 
        CoreProperty<T> property, 
        TimeRange range, 
        int sampleCount,
        Span<T> output)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);

        var key = (target, (CoreProperty)property);
        
        if (_compiledAnimations.TryGetValue(key, out var compiled))
        {
            compiled.SampleBuffer(range, sampleCount, output);
        }
        else
        {
            // No animation found, fill with current value
            var currentValue = target.GetValue(property);
            output.Fill(currentValue);
        }
    }

    private sealed class CompiledAnimation
    {
        private readonly IAnimation _animation;
        private readonly TimeRange _range;
        private readonly int _sampleRate;
        private readonly Dictionary<int, object> _sampleCache = new();

        public CompiledAnimation(IAnimation animation, TimeRange range, int sampleRate)
        {
            _animation = animation;
            _range = range;
            _sampleRate = sampleRate;
        }

        public object Sample(TimeSpan time)
        {
            // Convert time to relative time within the range
            var relativeTime = time - _range.Start;
            
            // Clamp to valid range
            if (relativeTime < TimeSpan.Zero)
                relativeTime = TimeSpan.Zero;
            else if (relativeTime > _range.Duration)
                relativeTime = _range.Duration;

            // Use the animation's interpolation method
            if (_animation is IAnimation<float> floatAnimation)
            {
                return floatAnimation.Interpolate(relativeTime) ?? 0f;
            }
            else if (_animation is IAnimation<double> doubleAnimation)
            {
                return doubleAnimation.Interpolate(relativeTime) ?? 0.0;
            }
            else if (_animation is IAnimation<int> intAnimation)
            {
                return intAnimation.Interpolate(relativeTime) ?? 0;
            }
            
            throw new NotSupportedException($"Animation type {_animation.GetType()} is not supported by AnimationSampler.");
        }

        public void SampleBuffer<T>(TimeRange range, int sampleCount, Span<T> output)
            where T : struct
        {
            if (sampleCount == 0)
                return;

            var timeStep = range.Duration.TotalSeconds / sampleCount;
            
            for (int i = 0; i < sampleCount; i++)
            {
                var sampleTime = range.Start + TimeSpan.FromSeconds(i * timeStep);
                
                // Check cache first
                if (_sampleCache.TryGetValue(i, out var cachedValue))
                {
                    output[i] = (T)cachedValue;
                }
                else
                {
                    var value = Sample(sampleTime);
                    _sampleCache[i] = value;
                    output[i] = (T)value;
                }
            }
        }
    }
}