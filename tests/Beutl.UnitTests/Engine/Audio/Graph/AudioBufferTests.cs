using System;
using Beutl.Audio.Graph;
using Beutl.Audio.Graph.Exceptions;

namespace Beutl.UnitTests.Engine.Audio.Graph;

[TestFixture]
public class AudioBufferTests
{
    [Test]
    public void Constructor_ValidParameters_CreatesBuffer()
    {
        // Arrange & Act
        using var buffer = new AudioBuffer(44100, 2, 1024);
        
        // Assert
        Assert.That(buffer.SampleRate, Is.EqualTo(44100));
        Assert.That(buffer.ChannelCount, Is.EqualTo(2));
        Assert.That(buffer.SampleCount, Is.EqualTo(1024));
    }

    [Test]
    public void Constructor_InvalidSampleRate_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioBuffer(0, 2, 1024));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioBuffer(-1, 2, 1024));
    }

    [Test]
    public void Constructor_InvalidChannelCount_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioBuffer(44100, 0, 1024));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioBuffer(44100, -1, 1024));
    }

    [Test]
    public void Constructor_InvalidSampleCount_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioBuffer(44100, 2, -1));
    }

    [Test]
    public void GetChannelData_ValidChannel_ReturnsCorrectSpan()
    {
        // Arrange
        using var buffer = new AudioBuffer(44100, 2, 1024);
        
        // Act
        var leftChannel = buffer.GetChannelData(0);
        var rightChannel = buffer.GetChannelData(1);
        
        // Assert
        Assert.That(leftChannel.Length, Is.EqualTo(1024));
        Assert.That(rightChannel.Length, Is.EqualTo(1024));
    }

    [Test]
    public void GetChannelData_InvalidChannel_ThrowsException()
    {
        // Arrange
        using var buffer = new AudioBuffer(44100, 2, 1024);
        
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetChannelData(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.GetChannelData(2));
    }

    [Test]
    public void GetChannelData_WriteAndRead_DataPersists()
    {
        // Arrange
        using var buffer = new AudioBuffer(44100, 2, 1024);
        var leftChannel = buffer.GetChannelData(0);
        
        // Act
        leftChannel[0] = 0.5f;
        leftChannel[100] = -0.3f;
        leftChannel[1023] = 0.8f;
        
        var readChannel = buffer.GetChannelData(0);
        
        // Assert
        Assert.That(readChannel[0], Is.EqualTo(0.5f));
        Assert.That(readChannel[100], Is.EqualTo(-0.3f));
        Assert.That(readChannel[1023], Is.EqualTo(0.8f));
    }

    [Test]
    public void Clear_BufferWithData_ClearsAllChannels()
    {
        // Arrange
        using var buffer = new AudioBuffer(44100, 2, 1024);
        var leftChannel = buffer.GetChannelData(0);
        var rightChannel = buffer.GetChannelData(1);
        
        leftChannel[0] = 0.5f;
        rightChannel[0] = -0.3f;
        
        // Act
        buffer.Clear();
        
        // Assert
        Assert.That(leftChannel[0], Is.EqualTo(0f));
        Assert.That(rightChannel[0], Is.EqualTo(0f));
    }

    [Test]
    public void CopyTo_CompatibleBuffer_CopiesData()
    {
        // Arrange
        using var source = new AudioBuffer(44100, 2, 1024);
        using var destination = new AudioBuffer(44100, 2, 1024);
        
        var sourceLeft = source.GetChannelData(0);
        sourceLeft[0] = 0.5f;
        sourceLeft[100] = -0.3f;
        
        // Act
        source.CopyTo(destination);
        
        // Assert
        var destLeft = destination.GetChannelData(0);
        Assert.That(destLeft[0], Is.EqualTo(0.5f));
        Assert.That(destLeft[100], Is.EqualTo(-0.3f));
    }

    [Test]
    public void CopyTo_IncompatibleSampleRate_ThrowsException()
    {
        // Arrange
        using var source = new AudioBuffer(44100, 2, 1024);
        using var destination = new AudioBuffer(48000, 2, 1024);
        
        // Act & Assert
        Assert.Throws<ArgumentException>(() => source.CopyTo(destination));
    }

    [Test]
    public void CopyTo_IncompatibleChannelCount_ThrowsException()
    {
        // Arrange
        using var source = new AudioBuffer(44100, 2, 1024);
        using var destination = new AudioBuffer(44100, 1, 1024);
        
        // Act & Assert
        Assert.Throws<ArgumentException>(() => source.CopyTo(destination));
    }

    [Test]
    public void CopyTo_IncompatibleSampleCount_ThrowsException()
    {
        // Arrange
        using var source = new AudioBuffer(44100, 2, 1024);
        using var destination = new AudioBuffer(44100, 2, 512);
        
        // Act & Assert
        Assert.Throws<ArgumentException>(() => source.CopyTo(destination));
    }

    [Test]
    public void Dispose_AccessAfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var buffer = new AudioBuffer(44100, 2, 1024);
        buffer.Dispose();
        
        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => buffer.GetChannelData(0));
        Assert.Throws<ObjectDisposedException>(() => buffer.Clear());
    }

    [Test]
    public void GetChannelMemory_ValidChannel_ReturnsCorrectMemory()
    {
        // Arrange
        using var buffer = new AudioBuffer(44100, 2, 1024);
        
        // Act
        var leftMemory = buffer.GetChannelMemory(0);
        var rightMemory = buffer.GetChannelMemory(1);
        
        // Assert
        Assert.That(leftMemory.Length, Is.EqualTo(1024));
        Assert.That(rightMemory.Length, Is.EqualTo(1024));
    }

    [Test]
    public void ZeroSampleCount_ValidBuffer_Works()
    {
        // Arrange & Act
        using var buffer = new AudioBuffer(44100, 2, 0);
        
        // Assert
        Assert.That(buffer.SampleCount, Is.EqualTo(0));
        Assert.That(buffer.GetChannelData(0).Length, Is.EqualTo(0));
    }
}