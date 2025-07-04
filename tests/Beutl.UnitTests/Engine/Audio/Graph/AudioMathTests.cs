using System;
using Beutl.Audio.Graph.Math;

namespace Beutl.UnitTests.Engine.Audio.Graph;

[TestFixture]
public class AudioMathTests
{
    [Test]
    public void AddWithGain_ValidInput_AddsCorrectly()
    {
        // Arrange
        float[] input = [0.5f, -0.3f, 0.8f, -0.1f];
        float[] output = [0.1f, 0.2f, 0.3f, 0.4f];
        float gain = 2.0f;
        
        var expectedOutput = new float[] { 1.1f, -0.4f, 1.9f, 0.2f }; // output + input * gain
        
        // Act
        AudioMath.AddWithGain(input, output, gain);
        
        // Assert
        for (int i = 0; i < expectedOutput.Length; i++)
        {
            Assert.That(output[i], Is.EqualTo(expectedOutput[i]).Within(0.0001f));
        }
    }

    [Test]
    public void AddWithGain_MismatchedLengths_ThrowsException()
    {
        // Arrange
        float[] input = [0.5f, -0.3f];
        float[] output = [0.1f, 0.2f, 0.3f];
        
        // Act & Assert
        Assert.Throws<ArgumentException>(() => AudioMath.AddWithGain(input, output, 1.0f));
    }

    [Test]
    public void MultiplyBuffers_ValidInput_MultipliesCorrectly()
    {
        // Arrange
        float[] input = [0.5f, -0.3f, 0.8f, -0.1f];
        float[] gains = [2.0f, 0.5f, 1.5f, 3.0f];
        float[] output = new float[4];
        
        var expectedOutput = new float[] { 1.0f, -0.15f, 1.2f, -0.3f };
        
        // Act
        AudioMath.MultiplyBuffers(input, gains, output);
        
        // Assert
        for (int i = 0; i < expectedOutput.Length; i++)
        {
            Assert.That(output[i], Is.EqualTo(expectedOutput[i]).Within(0.0001f));
        }
    }

    [Test]
    public void ApplyGain_ValidInput_AppliesGainCorrectly()
    {
        // Arrange
        float[] buffer = [0.5f, -0.3f, 0.8f, -0.1f];
        float gain = 2.0f;
        
        var expectedBuffer = new float[] { 1.0f, -0.6f, 1.6f, -0.2f };
        
        // Act
        AudioMath.ApplyGain(buffer, gain);
        
        // Assert
        for (int i = 0; i < expectedBuffer.Length; i++)
        {
            Assert.That(buffer[i], Is.EqualTo(expectedBuffer[i]).Within(0.0001f));
        }
    }

    [Test]
    public void MixBuffers_ValidInput_MixesCorrectly()
    {
        // Arrange
        float[] input1 = [0.5f, -0.3f, 0.8f, -0.1f];
        float[] input2 = [0.2f, 0.4f, -0.6f, 0.3f];
        float[] output = new float[4];
        float mix1 = 0.6f;
        float mix2 = 0.4f;
        
        var expectedOutput = new float[] { 0.38f, -0.02f, 0.24f, 0.06f }; // input1 * 0.6 + input2 * 0.4
        
        // Act
        AudioMath.MixBuffers(input1, input2, output, mix1, mix2);
        
        // Assert
        for (int i = 0; i < expectedOutput.Length; i++)
        {
            Assert.That(output[i], Is.EqualTo(expectedOutput[i]).Within(0.0001f));
        }
    }

    [Test]
    public void ConvertDbToLinear_ValidInput_ConvertsCorrectly()
    {
        // Arrange & Act
        var result0dB = AudioMath.ConvertDbToLinear(0f);
        var result6dB = AudioMath.ConvertDbToLinear(6f);
        var resultMinus6dB = AudioMath.ConvertDbToLinear(-6f);
        var resultMinus20dB = AudioMath.ConvertDbToLinear(-20f);
        
        // Assert
        Assert.That(result0dB, Is.EqualTo(1.0f).Within(0.0001f));
        Assert.That(result6dB, Is.EqualTo(1.995f).Within(0.01f));
        Assert.That(resultMinus6dB, Is.EqualTo(0.501f).Within(0.01f));
        Assert.That(resultMinus20dB, Is.EqualTo(0.1f).Within(0.001f));
    }

    [Test]
    public void ConvertLinearToDb_ValidInput_ConvertsCorrectly()
    {
        // Arrange & Act
        var result1 = AudioMath.ConvertLinearToDb(1.0f);
        var result2 = AudioMath.ConvertLinearToDb(2.0f);
        var resultHalf = AudioMath.ConvertLinearToDb(0.5f);
        var resultZero = AudioMath.ConvertLinearToDb(0.0f);
        
        // Assert
        Assert.That(result1, Is.EqualTo(0.0f).Within(0.0001f));
        Assert.That(result2, Is.EqualTo(6.02f).Within(0.01f));
        Assert.That(resultHalf, Is.EqualTo(-6.02f).Within(0.01f));
        Assert.That(resultZero, Is.EqualTo(-100f));
    }

    [Test]
    public void CalculateRms_ValidInput_CalculatesCorrectly()
    {
        // Arrange
        float[] samples = [0.5f, -0.5f, 0.3f, -0.3f];
        float expectedRms = MathF.Sqrt((0.25f + 0.25f + 0.09f + 0.09f) / 4f);
        
        // Act
        var result = AudioMath.CalculateRms(samples);
        
        // Assert
        Assert.That(result, Is.EqualTo(expectedRms).Within(0.0001f));
    }

    [Test]
    public void CalculateRms_EmptyInput_ReturnsZero()
    {
        // Arrange
        float[] samples = [];
        
        // Act
        var result = AudioMath.CalculateRms(samples);
        
        // Assert
        Assert.That(result, Is.EqualTo(0f));
    }

    [Test]
    public void FindPeak_ValidInput_FindsCorrectPeak()
    {
        // Arrange
        float[] samples = [0.5f, -0.8f, 0.3f, -0.1f, 0.7f];
        
        // Act
        var result = AudioMath.FindPeak(samples);
        
        // Assert
        Assert.That(result, Is.EqualTo(0.8f));
    }

    [Test]
    public void FindPeak_EmptyInput_ReturnsZero()
    {
        // Arrange
        float[] samples = [];
        
        // Act
        var result = AudioMath.FindPeak(samples);
        
        // Assert
        Assert.That(result, Is.EqualTo(0f));
    }

    [Test]
    public void ApplyLimiter_ValidInput_LimitsCorrectly()
    {
        // Arrange
        float[] buffer = [0.5f, 1.5f, -1.2f, 0.8f];
        float threshold = 1.0f;
        float ratio = 4.0f;
        
        // Act
        AudioMath.ApplyLimiter(buffer, threshold, ratio);
        
        // Assert
        Assert.That(buffer[0], Is.EqualTo(0.5f)); // Below threshold, unchanged
        Assert.That(buffer[1], Is.EqualTo(1.125f).Within(0.001f)); // 1.0 + (1.5-1.0)/4
        Assert.That(buffer[2], Is.EqualTo(-1.05f).Within(0.001f)); // -(1.0 + (1.2-1.0)/4)
        Assert.That(buffer[3], Is.EqualTo(0.8f)); // Below threshold, unchanged
    }

    [Test]
    public void ApplySoftClipper_ValidInput_ClipsCorrectly()
    {
        // Arrange
        float[] buffer = [0.5f, 1.5f, -1.2f, 0.7f];
        float threshold = 0.8f;
        
        // Act
        AudioMath.ApplySoftClipper(buffer, threshold);
        
        // Assert
        Assert.That(buffer[0], Is.EqualTo(0.5f)); // Below threshold, unchanged
        Assert.That(buffer[1], Is.LessThan(1.5f)); // Should be clipped
        Assert.That(buffer[2], Is.GreaterThan(-1.2f)); // Should be clipped
        Assert.That(buffer[3], Is.EqualTo(0.7f)); // Below threshold, unchanged
    }

    [Test]
    public void Normalize_ValidInput_NormalizesCorrectly()
    {
        // Arrange
        float[] buffer = [0.25f, 0.5f, -0.25f, 0.125f];
        float targetLevel = 1.0f;
        
        // Act
        AudioMath.Normalize(buffer, targetLevel);
        
        // Assert
        float expectedGain = 1.0f / 0.5f; // Peak was 0.5
        Assert.That(buffer[0], Is.EqualTo(0.25f * expectedGain).Within(0.0001f));
        Assert.That(buffer[1], Is.EqualTo(1.0f).Within(0.0001f)); // Peak should be 1.0
        Assert.That(buffer[2], Is.EqualTo(-0.5f).Within(0.0001f));
        Assert.That(buffer[3], Is.EqualTo(0.25f).Within(0.0001f));
    }

    [Test]
    public void FadeIn_ValidInput_AppliesFadeCorrectly()
    {
        // Arrange
        float[] buffer = [1.0f, 1.0f, 1.0f, 1.0f];
        int fadeLength = 4;
        
        // Act
        AudioMath.FadeIn(buffer, fadeLength);
        
        // Assert
        Assert.That(buffer[0], Is.EqualTo(0.0f)); // 0/4 * 1.0
        Assert.That(buffer[1], Is.EqualTo(0.25f)); // 1/4 * 1.0
        Assert.That(buffer[2], Is.EqualTo(0.5f)); // 2/4 * 1.0
        Assert.That(buffer[3], Is.EqualTo(0.75f)); // 3/4 * 1.0
    }

    [Test]
    public void FadeOut_ValidInput_AppliesFadeCorrectly()
    {
        // Arrange
        float[] buffer = [1.0f, 1.0f, 1.0f, 1.0f];
        int fadeLength = 4;
        
        // Act
        AudioMath.FadeOut(buffer, fadeLength);
        
        // Assert
        Assert.That(buffer[0], Is.EqualTo(1.0f)); // 1 - 0/4 * 1.0
        Assert.That(buffer[1], Is.EqualTo(0.75f)); // 1 - 1/4 * 1.0
        Assert.That(buffer[2], Is.EqualTo(0.5f)); // 1 - 2/4 * 1.0
        Assert.That(buffer[3], Is.EqualTo(0.25f)); // 1 - 3/4 * 1.0
    }

    [Test]
    public void FadeIn_FadeLengthLongerThanBuffer_FadesEntireBuffer()
    {
        // Arrange
        float[] buffer = [1.0f, 1.0f];
        int fadeLength = 4; // Longer than buffer
        
        // Act
        AudioMath.FadeIn(buffer, fadeLength);
        
        // Assert
        Assert.That(buffer[0], Is.EqualTo(0.0f));
        Assert.That(buffer[1], Is.EqualTo(0.5f));
    }

    [Test]
    public void LargeBuffer_PerformanceTest_CompletesInReasonableTime()
    {
        // Arrange
        const int bufferSize = 1024 * 1024; // 1M samples
        var buffer = new float[bufferSize];
        for (int i = 0; i < bufferSize; i++)
        {
            buffer[i] = (float)Math.Sin(2 * Math.PI * i / 1000);
        }
        
        var startTime = DateTime.UtcNow;
        
        // Act
        AudioMath.ApplyGain(buffer, 0.5f);
        var rms = AudioMath.CalculateRms(buffer);
        var peak = AudioMath.FindPeak(buffer);
        
        var endTime = DateTime.UtcNow;
        var elapsed = endTime - startTime;
        
        // Assert
        Assert.That(elapsed.TotalMilliseconds, Is.LessThan(100)); // Should complete in under 100ms
        Assert.That(rms, Is.GreaterThan(0));
        Assert.That(peak, Is.GreaterThan(0));
    }
}