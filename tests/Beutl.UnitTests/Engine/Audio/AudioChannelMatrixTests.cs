using Beutl.Audio;
using static Beutl.Audio.AudioSpeakers;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class AudioChannelMatrixTests
{
    private const float Gain = 0.70710677f;

    [Test]
    public void Surround51ToStereo_FoldsCenterAndBackAndDropsLfe()
    {
        var matrix = AudioChannelMatrix.Create(AudioChannelLayout.Surround51, AudioChannelLayout.Stereo);
        AssertRow(matrix, 0, [1, 0, Gain, 0, Gain, 0]);
        AssertRow(matrix, 1, [0, 1, Gain, 0, 0, Gain]);
        using var input = AudioTestBuffers.CreateBuffer(6, 3, (ch, i) => ch == 3 ? float.NaN : (ch + 1) * (i + 1));
        using var output = matrix.Apply(input);
        Assert.That(output.SampleRate, Is.EqualTo(input.SampleRate));
        Assert.That(output.SampleCount, Is.EqualTo(3));
        Assert.That(output.ChannelCount, Is.EqualTo(2));
        for (int i = 0; i < 3; i++)
        {
            Assert.That(output.GetChannelData(0)[i], Is.EqualTo((1 + 3 * Gain + 5 * Gain) * (i + 1)).Within(0.00001f));
            Assert.That(output.GetChannelData(1)[i], Is.EqualTo((2 + 3 * Gain + 6 * Gain) * (i + 1)).Within(0.00001f));
        }
        Assert.That(input.GetChannelData(0)[0], Is.EqualTo(1), "the input remains alive and unmodified");
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void SurroundSubstitution_UsesUnityUnlessBothPairsContribute(bool backToSide, bool bothPairs)
    {
        var side51 = new AudioChannelLayout(6, FrontLeft | FrontRight | FrontCenter | LowFrequency | SideLeft | SideRight);
        var input = bothPairs ? AudioChannelLayout.Surround71 : backToSide ? AudioChannelLayout.Surround51 : side51;
        var output = backToSide ? side51 : AudioChannelLayout.Surround51;
        var matrix = AudioChannelMatrix.Create(input, output);
        var from = backToSide ? BackLeft : SideLeft;
        var to = backToSide ? SideLeft : BackLeft;
        Assert.That(matrix.GetCoefficient(output.IndexOf(to), input.IndexOf(from)), Is.EqualTo(bothPairs ? Gain : 1));
        Assert.That(matrix.GetCoefficient(output.IndexOf(backToSide ? SideRight : BackRight), input.IndexOf(backToSide ? BackRight : SideRight)),
            Is.EqualTo(bothPairs ? Gain : 1));
        if (bothPairs)
            Assert.That(matrix.GetCoefficient(output.IndexOf(to), input.IndexOf(to)), Is.EqualTo(1));
        using var buffer = AudioTestBuffers.CreateBuffer(input.ChannelCount, 1, (ch, _) => ch + 1);
        using var result = matrix.Apply(buffer);
        float expected = (input.IndexOf(from) + 1) * (bothPairs ? Gain : 1);
        if (bothPairs) expected += input.IndexOf(to) + 1;
        Assert.That(result.GetChannelData(output.IndexOf(to))[0], Is.EqualTo(expected));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(6)]
    [TestCase(9)]
    [TestCase(32)]
    public void EqualLayouts_AreIdentityAndPreserveEveryBit(int count)
    {
        CheckCopy(AudioChannelLayout.Discrete(count), AudioChannelLayout.Discrete(count), count);
        if (count <= 8)
            CheckCopy(AudioChannelLayout.GetDefault(count), AudioChannelLayout.GetDefault(count), count);
    }

    [Test]
    public void MonoToStereo_CopiesEveryBitAtUnity()
    {
        CheckCopy(AudioChannelLayout.Mono, AudioChannelLayout.Stereo, 1);
    }

    private static void CheckCopy(AudioChannelLayout inputLayout, AudioChannelLayout outputLayout, int count)
    {
        int[] bits = [unchecked((int)0x80000000), 0x7FC12345, 0x7F800000, unchecked((int)0xFF800000), 0x3E800000];
        using var input = AudioTestBuffers.CreateBuffer(count, bits.Length, (_, i) => BitConverter.Int32BitsToSingle(bits[i]));
        var matrix = AudioChannelMatrix.Create(inputLayout, outputLayout);
        Assert.That(matrix.IsIdentity, Is.EqualTo(inputLayout == outputLayout));
        using var output = matrix.Apply(input);
        Assert.That(output, Is.Not.SameAs(input));
        for (int ch = 0; ch < output.ChannelCount; ch++)
            Assert.That(output.GetChannelData(ch).ToArray().Select(BitConverter.SingleToInt32Bits), Is.EqualTo(bits));
    }

    [Test]
    public void MonoToSurround_CopiesOnlyToTheFrontPair()
    {
        var matrix = AudioChannelMatrix.Create(AudioChannelLayout.Mono, AudioChannelLayout.Surround51);
        for (int i = 0; i < 6; i++)
            Assert.That(matrix.GetCoefficient(i, 0), Is.EqualTo(i < 2 ? 1 : 0));
        var front = new AudioChannelLayout(3, FrontCenter | FrontLeftOfCenter | FrontRightOfCenter);
        var noPair = AudioChannelMatrix.Create(AudioChannelLayout.Mono, front);
        for (int i = 0; i < 3; i++)
            Assert.That(noPair.GetCoefficient(i, 0), Is.EqualTo(1));
    }

    [Test]
    public void StereoAndSurroundToMono_ComposeTheStereoFoldWithTheAverage()
    {
        var stereo = AudioChannelMatrix.Create(AudioChannelLayout.Stereo, AudioChannelLayout.Mono);
        AssertRow(stereo, 0, [0.5f, 0.5f]);
        var surround = AudioChannelMatrix.Create(AudioChannelLayout.Surround51, AudioChannelLayout.Mono);
        AssertRow(surround, 0, [0.5f, 0.5f, Gain, 0, Gain * 0.5f, Gain * 0.5f]);
        using var input = AudioTestBuffers.CreateBuffer(2, 1, (ch, _) => ch == 0 ? 0.25f : 0.75f);
        using var output = stereo.Apply(input);
        Assert.That(output.GetChannelData(0)[0], Is.EqualTo(0.5f));
    }

    [TestCase(6)]
    [TestCase(9)]
    [TestCase(16)]
    public void DiscreteInput_UsesDefaultOrFirstTwoChannels(int count)
    {
        var matrix = AudioChannelMatrix.Create(AudioChannelLayout.Discrete(count), AudioChannelLayout.Stereo);
        Assert.That(matrix.IsIdentity, Is.False);
        for (int ch = 0; ch < count; ch++)
        {
            float left = ch == 0 ? 1 : count == 6 && (ch == 2 || ch == 4) ? Gain : 0;
            float right = ch == 1 ? 1 : count == 6 && (ch == 2 || ch == 5) ? Gain : 0;
            Assert.That(matrix.GetCoefficient(0, ch), Is.EqualTo(left));
            Assert.That(matrix.GetCoefficient(1, ch), Is.EqualTo(right));
        }
    }

    [TestCase(6)]
    [TestCase(9)]
    public void DiscreteOutput_UsesDefaultOrFirstTwoChannelsAndClearsOtherRows(int count)
    {
        var matrix = AudioChannelMatrix.Create(AudioChannelLayout.Stereo, AudioChannelLayout.Discrete(count));
        using var input = AudioTestBuffers.CreateBuffer(2, 2, (ch, i) => ch + i + 1);
        using var output = AudioTestBuffers.CreateConstantBuffer(99, 2, count);
        matrix.Apply(input, output);
        for (int ch = 0; ch < count; ch++)
            Assert.That(output.GetChannelData(ch).ToArray(), Is.EqualTo(ch < 2 ? input.GetChannelData(ch).ToArray() : new float[2]));
    }

    [TestCase(FrontCenter, FrontLeft | FrontRight, Gain, Gain)]
    [TestCase(BackCenter, BackLeft | BackRight, Gain, Gain)]
    [TestCase(BackCenter, SideLeft | SideRight, Gain, Gain)]
    [TestCase(BackCenter, FrontLeft | FrontRight, 0.5f, 0.5f)]
    [TestCase(FrontLeftOfCenter, FrontLeft | FrontRight, 1f, 0f)]
    [TestCase(FrontRightOfCenter, FrontLeft | FrontRight, 0f, 1f)]
    [TestCase(FrontLeftOfCenter, FrontCenter | BackCenter, Gain, 0f)]
    [TestCase(FrontRightOfCenter, FrontCenter | BackCenter, Gain, 0f)]
    [TestCase(FrontLeftOfCenter, FrontRight | FrontCenter, 0f, 0f)]
    [TestCase(FrontRightOfCenter, FrontLeft | FrontCenter, 0f, 0f)]
    [TestCase(TopFrontLeft, FrontLeft | FrontRight, Gain, 0f)]
    [TestCase(TopFrontRight, FrontLeft | FrontRight, 0f, Gain)]
    [TestCase(TopFrontCenter, FrontCenter | BackCenter, Gain, 0f)]
    [TestCase(TopCenter, FrontCenter | BackCenter, Gain, 0f)]
    [TestCase(TopFrontCenter, FrontLeft | FrontRight, 0.5f, 0.5f)]
    [TestCase(TopCenter, FrontLeft | FrontRight, 0.5f, 0.5f)]
    [TestCase(TopBackLeft, BackLeft | BackRight, Gain, 0f)]
    [TestCase(TopBackRight, BackLeft | BackRight, 0f, Gain)]
    [TestCase(TopBackLeft, SideLeft | SideRight, Gain, 0f)]
    [TestCase(TopBackRight, SideLeft | SideRight, 0f, Gain)]
    [TestCase(TopBackLeft, FrontLeft | FrontRight, Gain, 0f)]
    [TestCase(TopBackRight, FrontLeft | FrontRight, 0f, Gain)]
    [TestCase(TopBackCenter, FrontCenter | BackCenter, 0f, Gain)]
    [TestCase(TopBackCenter, BackLeft | BackRight, 0.5f, 0.5f)]
    [TestCase(TopBackCenter, SideLeft | SideRight, 0.5f, 0.5f)]
    [TestCase(TopBackCenter, FrontLeft | FrontRight, 0.5f, 0.5f)]
    public void MissingSpeaker_FoldsToItsHorizontalDestination(AudioSpeakers speaker, AudioSpeakers outputMask, float first, float second)
    {
        // LFE keeps the input multichannel without contributing to this output.
        var input = new AudioChannelLayout(2, speaker | LowFrequency);
        var output = new AudioChannelLayout(2, outputMask);
        var matrix = AudioChannelMatrix.Create(input, output);
        Assert.That(matrix.GetCoefficient(0, input.IndexOf(speaker)), Is.EqualTo(first));
        Assert.That(matrix.GetCoefficient(1, input.IndexOf(speaker)), Is.EqualTo(second));
        Assert.That(matrix.GetCoefficient(0, input.IndexOf(LowFrequency)), Is.Zero);
        Assert.That(matrix.GetCoefficient(1, input.IndexOf(LowFrequency)), Is.Zero);
    }

    [Test]
    public void Apply_ValidatesBufferShapesAndSupportsEmptyBuffers()
    {
        var matrix = AudioChannelMatrix.Create(AudioChannelLayout.Surround51, AudioChannelLayout.Stereo);
        using var input = new AudioBuffer(48000, 6, 0);
        using var output = matrix.Apply(input);
        Assert.That(output.SampleCount, Is.Zero);
        Assert.That(output.ChannelCount, Is.EqualTo(2));
        Assert.Throws<ArgumentException>(() => matrix.Apply(output));
        using var wrongLength = new AudioBuffer(48000, 2, 1);
        using var wrongRate = new AudioBuffer(44100, 2, 0);
        Assert.Throws<ArgumentException>(() => matrix.Apply(input, input));
        Assert.Throws<ArgumentException>(() => matrix.Apply(input, wrongLength));
        Assert.Throws<ArgumentException>(() => matrix.Apply(input, wrongRate));
    }

    [Test]
    public void Apply_InPlace_DoesNotOverwriteInputsNeededByOtherRows()
    {
        var matrix = AudioChannelMatrix.Create(new AudioChannelLayout(3, FrontCenter | FrontLeftOfCenter | FrontRightOfCenter),
            new AudioChannelLayout(3, FrontLeft | FrontRight | LowFrequency));
        using var input = AudioTestBuffers.CreateBuffer(3, 1, (ch, _) => ch + 1);
        matrix.Apply(input, input);
        Assert.That(input.GetChannelData(0)[0], Is.EqualTo(2 + Gain));
        Assert.That(input.GetChannelData(1)[0], Is.EqualTo(3 + Gain));
        Assert.That(input.GetChannelData(2)[0], Is.Zero);
    }

    [Test]
    public void Apply_MixesInInputChannelOrder()
    {
        var matrix = AudioChannelMatrix.Create(AudioChannelLayout.Surround51, AudioChannelLayout.Stereo);
        using var input = new AudioBuffer(48000, 6, 1);
        input.GetChannelData(0)[0] = 1;
        input.GetChannelData(2)[0] = 1e20f;
        input.GetChannelData(4)[0] = -1e20f;
        using var output = matrix.Apply(input);
        // The front's unit contribution rounds away before the back channel cancels the center.
        Assert.That(output.GetChannelData(0)[0], Is.Zero);
    }

    private static void AssertRow(AudioChannelMatrix matrix, int row, float[] expected)
    {
        Assert.That(matrix.InputChannelCount, Is.EqualTo(expected.Length));
        for (int ch = 0; ch < expected.Length; ch++)
            Assert.That(matrix.GetCoefficient(row, ch), Is.EqualTo(expected[ch]), $"row {row}, input {ch}");
    }
}
