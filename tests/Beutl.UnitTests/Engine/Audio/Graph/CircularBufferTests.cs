using System;
using Beutl.Audio.Graph.Effects;

namespace Beutl.UnitTests.Engine.Audio.Graph;

[TestFixture]
public class CircularBufferTests
{
    [Test]
    public void Constructor_ValidLength_CreatesBuffer()
    {
        // Arrange & Act
        using var buffer = new CircularBuffer<float>(1024);
        
        // Assert
        Assert.That(buffer.Length, Is.GreaterThanOrEqualTo(1024));
        Assert.That(buffer.WriteIndex, Is.EqualTo(0));
    }

    [Test]
    public void Constructor_InvalidLength_ThrowsException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new CircularBuffer<float>(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CircularBuffer<float>(-1));
    }

    [Test]
    public void Constructor_PowerOfTwo_CreatesExactSize()
    {
        // Arrange & Act
        using var buffer = new CircularBuffer<float>(1024); // Already power of 2
        
        // Assert
        Assert.That(buffer.Length, Is.EqualTo(1024));
    }

    [Test]
    public void Constructor_NotPowerOfTwo_RoundsUpToPowerOfTwo()
    {
        // Arrange & Act
        using var buffer = new CircularBuffer<float>(1000);
        
        // Assert
        Assert.That(buffer.Length, Is.EqualTo(1024)); // Next power of 2
    }

    [Test]
    public void WriteAndRead_SingleValue_WorksCorrectly()
    {
        // Arrange
        using var buffer = new CircularBuffer<float>(4);
        
        // Act
        buffer.Write(0.5f);
        var result = buffer.Read(0);
        
        // Assert
        Assert.That(result, Is.EqualTo(0.5f));
    }

    [Test]
    public void WriteAndRead_MultipleValues_WorksCorrectly()
    {
        // Arrange
        using var buffer = new CircularBuffer<float>(4);
        
        // Act
        buffer.Write(0.1f);
        buffer.Write(0.2f);
        buffer.Write(0.3f);
        
        // Assert
        Assert.That(buffer.Read(0), Is.EqualTo(0.3f)); // Most recent
        Assert.That(buffer.Read(1), Is.EqualTo(0.2f)); // One back
        Assert.That(buffer.Read(2), Is.EqualTo(0.1f)); // Two back
    }

    [Test]
    public void Write_Wrapping_WorksCorrectly()
    {
        // Arrange
        using var buffer = new CircularBuffer<float>(4);
        
        // Act - Write more than buffer size
        buffer.Write(0.1f);
        buffer.Write(0.2f);
        buffer.Write(0.3f);
        buffer.Write(0.4f);
        buffer.Write(0.5f); // This should wrap around
        
        // Assert
        Assert.That(buffer.Read(0), Is.EqualTo(0.5f));
        Assert.That(buffer.Read(1), Is.EqualTo(0.4f));
        Assert.That(buffer.Read(2), Is.EqualTo(0.3f));
        Assert.That(buffer.Read(3), Is.EqualTo(0.2f));
    }

    [Test]
    public void Read_OutOfRange_ReturnsDefault()
    {
        // Arrange
        using var buffer = new CircularBuffer<float>(4);
        buffer.Write(0.5f);
        
        // Act & Assert
        Assert.That(buffer.Read(10), Is.EqualTo(0f)); // Out of range
        Assert.That(buffer.Read(4), Is.EqualTo(0f)); // Exactly at limit
    }

    [Test]
    public void Read_NegativeIndex_ThrowsException()
    {
        // Arrange
        using var buffer = new CircularBuffer<float>(4);
        
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Read(-1));
    }

    [Test]
    public void Clear_BufferWithData_ClearsAllData()
    {
        // Arrange
        using var buffer = new CircularBuffer<float>(4);
        buffer.Write(0.1f);
        buffer.Write(0.2f);
        buffer.Write(0.3f);
        
        // Act
        buffer.Clear();
        
        // Assert
        Assert.That(buffer.Read(0), Is.EqualTo(0f));
        Assert.That(buffer.Read(1), Is.EqualTo(0f));
        Assert.That(buffer.Read(2), Is.EqualTo(0f));
        Assert.That(buffer.WriteIndex, Is.EqualTo(0));
    }

    [Test]
    public void FillWithValue_ValidValue_FillsBuffer()
    {
        // Arrange
        using var buffer = new CircularBuffer<float>(4);
        
        // Act
        buffer.FillWithValue(0.7f);
        
        // Assert
        Assert.That(buffer.Read(0), Is.EqualTo(0.7f));
        Assert.That(buffer.Read(1), Is.EqualTo(0.7f));
        Assert.That(buffer.Read(2), Is.EqualTo(0.7f));
        Assert.That(buffer.Read(3), Is.EqualTo(0.7f));
    }

    [Test]
    public void GetInternalBuffer_ValidBuffer_ReturnsCorrectSpan()
    {
        // Arrange
        using var buffer = new CircularBuffer<float>(4);
        
        // Act
        var span = buffer.GetInternalBuffer();
        
        // Assert
        Assert.That(span.Length, Is.EqualTo(buffer.Length));
    }

    [Test]
    public void WriteIndex_MultipleWrites_AdvancesCorrectly()
    {
        // Arrange
        using var buffer = new CircularBuffer<float>(4);
        
        // Act & Assert
        Assert.That(buffer.WriteIndex, Is.EqualTo(0));
        
        buffer.Write(0.1f);
        Assert.That(buffer.WriteIndex, Is.EqualTo(1));
        
        buffer.Write(0.2f);
        Assert.That(buffer.WriteIndex, Is.EqualTo(2));
        
        buffer.Write(0.3f);
        Assert.That(buffer.WriteIndex, Is.EqualTo(3));
        
        buffer.Write(0.4f);
        Assert.That(buffer.WriteIndex, Is.EqualTo(0)); // Wrapped around
    }

    [Test]
    public void DelayEffect_Simulation_WorksCorrectly()
    {
        // Arrange
        using var buffer = new CircularBuffer<float>(8);
        int delaySamples = 3;
        float feedback = 0.5f;
        
        // Act - Simulate delay effect processing
        float[] input = [1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f];
        float[] output = new float[input.Length];
        
        for (int i = 0; i < input.Length; i++)
        {
            var delayed = buffer.Read(delaySamples);
            var feedbackSample = input[i] + delayed * feedback;
            buffer.Write(feedbackSample);
            output[i] = input[i] + delayed * 0.3f; // 30% wet mix
        }
        
        // Assert
        Assert.That(output[0], Is.EqualTo(1.0f)); // Original signal only
        Assert.That(output[1], Is.EqualTo(0.0f)); // No signal
        Assert.That(output[2], Is.EqualTo(0.0f)); // No signal
        Assert.That(output[3], Is.GreaterThan(0.0f)); // Should have delayed signal
    }

    [Test]
    public void Dispose_AccessAfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var buffer = new CircularBuffer<float>(4);
        buffer.Dispose();
        
        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => buffer.Write(0.5f));
        Assert.Throws<ObjectDisposedException>(() => buffer.Read(0));
        Assert.Throws<ObjectDisposedException>(() => buffer.Clear());
        Assert.Throws<ObjectDisposedException>(() => buffer.FillWithValue(0.5f));
        Assert.Throws<ObjectDisposedException>(() => buffer.GetInternalBuffer());
    }

    [Test]
    public void LargeBuffer_PerformanceTest_CompletesInReasonableTime()
    {
        // Arrange
        const int bufferSize = 1024 * 1024; // 1M samples
        using var buffer = new CircularBuffer<float>(bufferSize);
        
        var startTime = DateTime.UtcNow;
        
        // Act
        for (int i = 0; i < 10000; i++)
        {
            buffer.Write((float)Math.Sin(2 * Math.PI * i / 1000));
        }
        
        for (int i = 0; i < 10000; i++)
        {
            buffer.Read(i % 1000);
        }
        
        var endTime = DateTime.UtcNow;
        var elapsed = endTime - startTime;
        
        // Assert
        Assert.That(elapsed.TotalMilliseconds, Is.LessThan(100)); // Should complete in under 100ms
    }

    [Test]
    public void CircularBuffer_DifferentTypes_WorksCorrectly()
    {
        // Test with int
        using var intBuffer = new CircularBuffer<int>(4);
        intBuffer.Write(42);
        Assert.That(intBuffer.Read(0), Is.EqualTo(42));
        
        // Test with double
        using var doubleBuffer = new CircularBuffer<double>(4);
        doubleBuffer.Write(3.14159);
        Assert.That(doubleBuffer.Read(0), Is.EqualTo(3.14159).Within(0.00001));
    }
}