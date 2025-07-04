using System;
using System.Runtime.InteropServices;
using Beutl.Animation;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Effects;
using Beutl.Media;
using Moq;

namespace Beutl.UnitTests.Engine.Audio.Graph.Effects;

[TestFixture]
public class AudioDelayEffectTests
{
    // Simple no-op animation sampler for tests that don't need animation
    private class NoOpAnimationSampler : IAnimationSampler
    {
        public T Sample<T>(IAnimatable target, CoreProperty<T> property, TimeSpan time) where T : notnull
        {
            if (target is ICoreObject coreObject)
            {
                return coreObject.GetValue(property);
            }
            return default!;
        }
        
        public void SampleBuffer<T>(IAnimatable target, CoreProperty<T> property, TimeRange range, int sampleCount, Span<T> output) where T : struct
        {
            if (target is ICoreObject coreObject)
            {
                var value = coreObject.GetValue(property);
                output.Slice(0, Math.Min(sampleCount, output.Length)).Fill(value);
            }
        }
    }
    
    [Test]
    public void Constructor_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var effect = new AudioDelayEffect();
        
        // Assert
        Assert.That(effect.DelayTime, Is.EqualTo(200f));
        Assert.That(effect.Feedback, Is.EqualTo(50f));
        Assert.That(effect.DryMix, Is.EqualTo(60f));
        Assert.That(effect.WetMix, Is.EqualTo(40f));
        Assert.That(effect.IsEnabled, Is.True);
    }
    
    [Test]
    public void DelayTime_SetValue_ClampsToValidRange()
    {
        // Arrange
        var effect = new AudioDelayEffect();
        
        // Act & Assert - Test lower bound
        effect.DelayTime = -100f;
        Assert.That(effect.DelayTime, Is.EqualTo(0f));
        
        // Test upper bound
        effect.DelayTime = 10000f;
        Assert.That(effect.DelayTime, Is.EqualTo(5000f));
        
        // Test valid value
        effect.DelayTime = 500f;
        Assert.That(effect.DelayTime, Is.EqualTo(500f));
    }
    
    [Test]
    public void Feedback_SetValue_ClampsToValidRange()
    {
        // Arrange
        var effect = new AudioDelayEffect();
        
        // Act & Assert
        effect.Feedback = -10f;
        Assert.That(effect.Feedback, Is.EqualTo(0f));
        
        effect.Feedback = 150f;
        Assert.That(effect.Feedback, Is.EqualTo(100f));
        
        effect.Feedback = 75f;
        Assert.That(effect.Feedback, Is.EqualTo(75f));
    }
    
    [Test]
    public void CreateProcessor_ReturnsValidProcessor()
    {
        // Arrange
        var effect = new AudioDelayEffect();
        
        // Act
        var processor = effect.CreateProcessor();
        
        // Assert
        Assert.That(processor, Is.Not.Null);
        Assert.That(processor, Is.InstanceOf<IAudioEffectProcessor>());
    }
    
    [Test]
    public void Processor_Process_AppliesDelayEffect()
    {
        // Arrange
        var effect = new AudioDelayEffect
        {
            DelayTime = 10f,  // 10ms delay for simpler test
            Feedback = 0f,    // No feedback
            DryMix = 50f,     // 50% dry  
            WetMix = 50f      // 50% wet
        };
        
        var processor = effect.CreateProcessor();
        
        // Create buffers with constant signal
        using var inputBuffer = new AudioBuffer(1000, 2, 100);
        using var outputBuffer = new AudioBuffer(1000, 2, 100);
        
        // Fill with constant value
        var leftIn = inputBuffer.GetChannelData(0);
        var rightIn = inputBuffer.GetChannelData(1);
        for (int i = 0; i < 100; i++)
        {
            leftIn[i] = 1.0f;
            rightIn[i] = 1.0f;
        }
        
        var context = new AudioProcessContext(
            new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(0.1)), 
            1000, 
            new NoOpAnimationSampler());
        
        // Act
        processor.Process(inputBuffer, outputBuffer, context);
        
        // Assert - With constant input and delay, we should get a mix of dry and delayed signal
        var leftOut = outputBuffer.GetChannelData(0);
        var rightOut = outputBuffer.GetChannelData(1);
        
        // The first few samples will have reduced output due to empty delay buffer
        // But after the delay time, we should get full output (dry + wet of same signal = 1.0)
        bool foundFullSignal = false;
        for (int i = 20; i < 100; i++) // Check after initial delay period
        {
            if (Math.Abs(leftOut[i] - 1.0f) < 0.001f && Math.Abs(rightOut[i] - 1.0f) < 0.001f)
            {
                foundFullSignal = true;
                break;
            }
        }
        
        Assert.That(foundFullSignal, Is.True, "Should find full signal after delay period");
    }
    
    [Test]
    public void Processor_Process_AppliesDryWetMix()
    {
        // Arrange
        var effect = new AudioDelayEffect
        {
            DelayTime = 1f,   // 1ms delay
            Feedback = 0f,
            DryMix = 50f,     // 50% dry
            WetMix = 50f      // 50% wet
        };
        
        var processor = effect.CreateProcessor();
        
        using var inputBuffer = new AudioBuffer(44100, 2, 100);
        using var outputBuffer = new AudioBuffer(44100, 2, 100);
        
        // Fill with constant value
        var leftIn = inputBuffer.GetChannelData(0);
        var rightIn = inputBuffer.GetChannelData(1);
        for (int i = 0; i < 100; i++)
        {
            leftIn[i] = 1.0f;
            rightIn[i] = 1.0f;
        }
        
        var context = new AudioProcessContext(
            new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)), 
            44100, 
            new NoOpAnimationSampler());
        
        // Act
        processor.Process(inputBuffer, outputBuffer, context);
        
        // Assert - Check dry/wet mix behavior
        var leftOut = outputBuffer.GetChannelData(0);
        var rightOut = outputBuffer.GetChannelData(1);
        
        // With 1ms delay at 44100Hz = ~44 samples delay
        // The first 44 samples should only have dry signal (0.5)
        for (int i = 0; i < 10; i++) // Check first 10 samples
        {
            // output = input * 0.5 (dry) + delayed * 0.5 (wet, initially 0) = 0.5
            Assert.That(leftOut[i], Is.EqualTo(0.5f).Within(0.1f), $"Sample {i} should be ~0.5");
            Assert.That(rightOut[i], Is.EqualTo(0.5f).Within(0.1f), $"Sample {i} should be ~0.5");
        }
        
        // After delay period, we should see mix of dry and wet (both 1.0 input)
        for (int i = 50; i < 100; i++) // Check later samples
        {
            // output = input * 0.5 (dry) + input * 0.5 (wet) = 1.0
            Assert.That(leftOut[i], Is.EqualTo(1.0f).Within(0.1f), $"Sample {i} should be ~1.0");
            Assert.That(rightOut[i], Is.EqualTo(1.0f).Within(0.1f), $"Sample {i} should be ~1.0");
        }
    }
    
    [Test]
    public void Processor_Reset_ClearsDelayBuffer()
    {
        // Arrange
        var effect = new AudioDelayEffect
        {
            DelayTime = 100f,
            Feedback = 90f,   // High feedback to keep signal in buffer
            DryMix = 0f,
            WetMix = 100f
        };
        
        var processor = effect.CreateProcessor();
        
        using var inputBuffer = new AudioBuffer(1000, 2, 200);
        using var outputBuffer = new AudioBuffer(1000, 2, 200);
        
        // Process with impulse
        var leftIn = inputBuffer.GetChannelData(0);
        leftIn[0] = 1.0f;
        
        var context = new AudioProcessContext(
            new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(0.2)), 
            1000, 
            new NoOpAnimationSampler());
        
        processor.Process(inputBuffer, outputBuffer, context);
        
        // Act - Reset the processor
        processor.Reset();
        
        // Clear input and process again
        leftIn[0] = 0.0f;
        processor.Process(inputBuffer, outputBuffer, context);
        
        // Assert - Output should be silent after reset
        var leftOut = outputBuffer.GetChannelData(0);
        for (int i = 0; i < 200; i++)
        {
            Assert.That(leftOut[i], Is.EqualTo(0f).Within(0.001f));
        }
    }
    
    [Test]
    public void Processor_WithAnimationSampler_ProcessesSuccessfully()
    {
        // Arrange
        var effect = new AudioDelayEffect
        {
            DelayTime = 50f,
            Feedback = 25f,
            DryMix = 50f,
            WetMix = 50f
        };
        
        // Create a simple test animation sampler that returns constant values
        var sampler = new TestAnimationSampler(effect);
        
        var processor = effect.CreateProcessor();
        
        using var inputBuffer = new AudioBuffer(44100, 2, 100);
        using var outputBuffer = new AudioBuffer(44100, 2, 100);
        
        var context = new AudioProcessContext(
            new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(0.1)), 
            44100, 
            sampler);
        
        // Act & Assert - Should not throw
        Assert.DoesNotThrow(() => processor.Process(inputBuffer, outputBuffer, context));
    }
    
    // Test helper class for animation sampling
    private class TestAnimationSampler : IAnimationSampler
    {
        private readonly AudioDelayEffect _effect;
        
        public TestAnimationSampler(AudioDelayEffect effect)
        {
            _effect = effect;
        }
        
        public T Sample<T>(IAnimatable target, CoreProperty<T> property, TimeSpan time) where T : notnull
        {
            if (target == _effect && property is CoreProperty<float> floatProp)
            {
                if (floatProp.Equals(AudioDelayEffect.DelayTimeProperty))
                    return (T)(object)_effect.DelayTime;
                if (floatProp.Equals(AudioDelayEffect.FeedbackProperty))
                    return (T)(object)_effect.Feedback;
                if (floatProp.Equals(AudioDelayEffect.DryMixProperty))
                    return (T)(object)_effect.DryMix;
                if (floatProp.Equals(AudioDelayEffect.WetMixProperty))
                    return (T)(object)_effect.WetMix;
            }
            return default!;
        }
        
        public void SampleBuffer<T>(IAnimatable target, CoreProperty<T> property, TimeRange range, int sampleCount, Span<T> output) where T : struct
        {
            if (target == _effect && typeof(T) == typeof(float) && property is CoreProperty<float> floatProp)
            {
                var floatOutput = MemoryMarshal.Cast<T, float>(output);
                float value = 0f;
                
                if (floatProp.Equals(AudioDelayEffect.DelayTimeProperty))
                    value = _effect.DelayTime;
                else if (floatProp.Equals(AudioDelayEffect.FeedbackProperty))
                    value = _effect.Feedback;
                else if (floatProp.Equals(AudioDelayEffect.DryMixProperty))
                    value = _effect.DryMix;
                else if (floatProp.Equals(AudioDelayEffect.WetMixProperty))
                    value = _effect.WetMix;
                
                floatOutput.Slice(0, Math.Min(sampleCount, floatOutput.Length)).Fill(value);
            }
        }
    }
    
    [Test]
    public void Processor_Dispose_ReleasesResources()
    {
        // Arrange
        var effect = new AudioDelayEffect();
        var processor = effect.CreateProcessor();
        
        // Initialize processor
        using var inputBuffer = new AudioBuffer(44100, 2, 100);
        using var outputBuffer = new AudioBuffer(44100, 2, 100);
        var context = new AudioProcessContext(
            new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)), 
            44100, 
            new NoOpAnimationSampler());
        
        processor.Process(inputBuffer, outputBuffer, context);
        
        // Act
        processor.Dispose();
        
        // Assert - Should not throw when disposed
        Assert.DoesNotThrow(() => processor.Dispose());
    }
}