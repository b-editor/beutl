using System;
using Beutl.Audio.Effects;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Effects;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Moq;

namespace Beutl.UnitTests.Engine.Audio.Effects;

[TestFixture]
public class SoundEffectWrapperTests
{
    // Simple no-op animation sampler for tests
    private class NoOpAnimationSampler : Beutl.Audio.Graph.IAnimationSampler
    {
        public T Sample<T>(Beutl.Animation.IAnimatable target, Beutl.CoreProperty<T> property, TimeSpan time) where T : notnull
        {
            if (target is Beutl.ICoreObject coreObject)
            {
                return coreObject.GetValue(property);
            }
            return default!;
        }
        
        public void SampleBuffer<T>(Beutl.Animation.IAnimatable target, Beutl.CoreProperty<T> property, TimeRange range, int sampleCount, Span<T> output) where T : struct
        {
            if (target is Beutl.ICoreObject coreObject)
            {
                var value = coreObject.GetValue(property);
                output.Slice(0, Math.Min(sampleCount, output.Length)).Fill(value);
            }
        }
    }
    
    [Test]
    public void Constructor_ValidSoundEffect_WrapsCorrectly()
    {
        // Arrange
        var mockEffect = new Mock<ISoundEffect>();
        mockEffect.Setup(e => e.IsEnabled).Returns(true);
        
        // Act
        var wrapper = new SoundEffectWrapper(mockEffect.Object);
        
        // Assert
        Assert.That(wrapper.InnerEffect, Is.EqualTo(mockEffect.Object));
        Assert.That(wrapper.IsEnabled, Is.True);
    }
    
    [Test]
    public void Constructor_NullSoundEffect_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SoundEffectWrapper(null!));
    }
    
    [Test]
    public void CreateProcessor_ValidEffect_ReturnsProcessor()
    {
        // Arrange
        var mockProcessor = new Mock<ISoundProcessor>();
        var mockEffect = new Mock<ISoundEffect>();
        mockEffect.Setup(e => e.CreateProcessor()).Returns(mockProcessor.Object);
        var wrapper = new SoundEffectWrapper(mockEffect.Object);
        
        // Act
        var processor = wrapper.CreateProcessor();
        
        // Assert
        Assert.That(processor, Is.Not.Null);
        Assert.That(processor, Is.InstanceOf<IAudioEffectProcessor>());
    }
    
    [Test]
    public void ProcessorWrapper_Process_ConvertsPcmToAudioBuffer()
    {
        // Arrange
        var testProcessor = new TestSoundProcessor();
        var mockEffect = new Mock<ISoundEffect>();
        mockEffect.Setup(e => e.CreateProcessor()).Returns(testProcessor);
        
        var wrapper = new SoundEffectWrapper(mockEffect.Object);
        var processor = wrapper.CreateProcessor();
        
        // Create test buffers
        using var inputBuffer = new AudioBuffer(44100, 2, 100);
        using var outputBuffer = new AudioBuffer(44100, 2, 100);
        
        // Fill input buffer with test data
        var leftChannel = inputBuffer.GetChannelData(0);
        var rightChannel = inputBuffer.GetChannelData(1);
        for (int i = 0; i < 100; i++)
        {
            leftChannel[i] = 0.8f;
            rightChannel[i] = 0.6f;
        }
        
        var context = new AudioProcessContext(new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)), 44100, new NoOpAnimationSampler());
        
        // Act
        processor.Process(inputBuffer, outputBuffer, context);
        
        // Assert
        Assert.That(testProcessor.ProcessCalled, Is.True);
        
        // Verify output was written correctly (halved values)
        var outLeft = outputBuffer.GetChannelData(0);
        var outRight = outputBuffer.GetChannelData(1);
        for (int i = 0; i < 100; i++)
        {
            Assert.That(outLeft[i], Is.EqualTo(0.4f).Within(0.001f));
            Assert.That(outRight[i], Is.EqualTo(0.3f).Within(0.001f));
        }
    }
    
    [Test]
    public void ProcessorWrapper_Process_HandlesMonoInput()
    {
        // Arrange
        var testProcessor = new TestSoundProcessor { PassThrough = true };
        var mockEffect = new Mock<ISoundEffect>();
        mockEffect.Setup(e => e.CreateProcessor()).Returns(testProcessor);
        
        var wrapper = new SoundEffectWrapper(mockEffect.Object);
        var processor = wrapper.CreateProcessor();
        
        // Create mono buffers
        using var inputBuffer = new AudioBuffer(44100, 1, 100);
        using var outputBuffer = new AudioBuffer(44100, 1, 100);
        
        // Fill input buffer
        var monoChannel = inputBuffer.GetChannelData(0);
        for (int i = 0; i < 100; i++)
        {
            monoChannel[i] = 0.5f;
        }
        
        var context = new AudioProcessContext(new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)), 44100, new NoOpAnimationSampler());
        
        // Act
        processor.Process(inputBuffer, outputBuffer, context);
        
        // Assert - mono should be duplicated to stereo for processing
        var outChannel = outputBuffer.GetChannelData(0);
        for (int i = 0; i < 100; i++)
        {
            Assert.That(outChannel[i], Is.EqualTo(0.5f).Within(0.001f));
        }
    }
    
    [Test]
    public void ProcessorWrapper_Dispose_DisposesResources()
    {
        // Arrange
        var mockProcessor = new Mock<ISoundProcessor>();
        var mockEffect = new Mock<ISoundEffect>();
        mockEffect.Setup(e => e.CreateProcessor()).Returns(mockProcessor.Object);
        
        var wrapper = new SoundEffectWrapper(mockEffect.Object);
        var processor = wrapper.CreateProcessor();
        
        // Act
        processor.Dispose();
        
        // Assert
        mockProcessor.Verify(p => p.Dispose(), Times.Once);
    }
    
    // Test implementation of ISoundProcessor
    private class TestSoundProcessor : ISoundProcessor
    {
        public bool ProcessCalled { get; private set; }
        public bool PassThrough { get; set; }
        
        public void Process(in Pcm<Stereo32BitFloat> src, out Pcm<Stereo32BitFloat> dst)
        {
            ProcessCalled = true;
            
            if (PassThrough)
            {
                dst = src;
            }
            else
            {
                dst = new Pcm<Stereo32BitFloat>(src.SampleRate, src.NumSamples);
                
                // Halve the input values
                var srcSpan = src.DataSpan;
                var dstSpan = dst.DataSpan;
                for (int i = 0; i < srcSpan.Length; i++)
                {
                    dstSpan[i] = new Stereo32BitFloat(srcSpan[i].Left * 0.5f, srcSpan[i].Right * 0.5f);
                }
            }
        }
        
        public void Dispose()
        {
        }
    }
}