using System.Runtime.InteropServices;
using Beutl.Audio;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.SharedMemory;
using Beutl.FFmpegWorker.Decoding;
using Beutl.FFmpegWorker.Handlers;
using Beutl.Media.Decoding;
using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;

namespace Beutl.FFmpegWorker.Tests;

[TestFixture]
public sealed class FFmpegReaderAudioConversionTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp() => FFmpegTestHelpers.RequireFFmpeg();

    [TestCase(44100, 2, true)]
    [TestCase(48000, 2, true)]
    [TestCase(48000, 1, true)]
    [TestCase(48000, 6, true)]
    [TestCase(48000, 6, false)]
    public void ReusedConverter_PreservesSamplesAcrossChangingFrameSizes(int sampleRate, int channels, bool positions)
    {
        AVChannelLayout layout = Layout(channels, positions);
        using var converter = new FFmpegReader.AudioFrameConverter(sampleRate, FFmpegReader.GetOutputLayout(layout));
        int sequence = 0;
        foreach (int samples in new[] { 1024, 128, 4096, 37, 1024 })
        {
            float[] markers = Enumerable.Range(0, channels).Select(c => ++sequence / 16f).ToArray();
            using var input = Frame(layout, samples, sampleRate, markers);
            using MediaFrame output = converter.ConvertFrame(input);
            AssertSamples(output, samples, sampleRate, markers);
            Assert.That(output.ChLayout.order, Is.EqualTo(layout.order));
            Assert.That(FFmpegReader.GetChannelLayout(output.ChLayout), Is.EqualTo(positions
                ? AudioChannelLayout.GetDefault(channels) : AudioChannelLayout.Discrete(channels)));
        }
    }

    [TestCase("native")]
    [TestCase("reversed")]
    [TestCase("unknown")]
    public unsafe void CustomLayout_PreservesOrder_WithoutSharingTheMap(string order)
    {
        using var first = Frame(CustomLayout(order), 32, 48000, [1, 2, 3, 4, 5, 6]);
        AVChannelLayout outputLayout = FFmpegReader.GetOutputLayout(first.ChLayout);
        Assert.That(first.ChLayout.order, Is.EqualTo(AVChannelOrder.AV_CHANNEL_ORDER_CUSTOM));
        Assert.That(first.ChLayout.u.map[0].id, Is.EqualTo(order switch
        {
            "native" => AVChannel.AV_CHAN_FRONT_LEFT,
            "reversed" => AVChannel.AV_CHAN_FRONT_RIGHT,
            _ => AVChannel.AV_CHAN_UNKNOWN
        }), "normalization must not free or change the decoder's map");
        Assert.That(outputLayout.order, Is.EqualTo(order == "native"
            ? AVChannelOrder.AV_CHANNEL_ORDER_NATIVE : AVChannelOrder.AV_CHANNEL_ORDER_UNSPEC));
        Assert.That(FFmpegReader.GetChannelLayout(outputLayout), Is.EqualTo(order == "native"
            ? AudioChannelLayout.Surround51 : AudioChannelLayout.Discrete(6)));

        using var converter = new FFmpegReader.AudioFrameConverter(48000, outputLayout);
        using (MediaFrame output = converter.ConvertFrame(first))
            AssertSamples(output, 32, 48000, [1, 2, 3, 4, 5, 6]);
        first.Dispose();
        // The next output must still work after both the original map and first output were freed.
        using var second = Frame(CustomLayout(order), 19, 48000, [6, 5, 4, 3, 2, 1]);
        using MediaFrame next = converter.ConvertFrame(second);
        AssertSamples(next, 19, 48000, [6, 5, 4, 3, 2, 1]);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ChangedFrameLayout_IsRemixedIntoTheFixedOutput(bool custom)
    {
        AVChannelLayout outputLayout = Layout(6);
        using var converter = new FFmpegReader.AudioFrameConverter(48000, outputLayout);
        using var first = Frame(outputLayout, 32, 48000, [1, 2, 3, 4, 5, 6]);
        using (MediaFrame output = converter.ConvertFrame(first))
            AssertSamples(output, 32, 48000, [1, 2, 3, 4, 5, 6]);

        using var changed = custom
            ? Frame(CustomLayout("reversed"), 17, 48000, [2, 1, 3, 4, 5, 6])
            : Frame(Layout(2), 17, 48000, [1, 2]);
        using (MediaFrame output = converter.ConvertFrame(changed))
        {
            Assert.That(output.ChLayout.u.mask, Is.EqualTo(outputLayout.u.mask));
            AssertSamples(output, 17, 48000, custom ? [1, 2, 3, 4, 5, 6] : [1, 2, 0, 0, 0, 0]);
        }
        using MediaFrame restored = converter.ConvertFrame(first);
        AssertSamples(restored, 32, 48000, [1, 2, 3, 4, 5, 6]);
    }

    [TestCase(0x3FUL, 6, 0x3FU)]
    [TestCase(0x60FUL, 6, 0x60FU)]
    [TestCase(0x3FFFFUL, 18, 0x3FFFFU)]
    [TestCase(0x60000000UL, 2, 3U)] // DL/DR aliases
    [TestCase(0x40000001UL, 2, 3U)] // FL with DR preserves order
    [TestCase(0x20000002UL, 2, 0U)] // FR precedes DL, so a stereo mask would mislabel the samples
    [TestCase(0x20000003UL, 3, 0U)] // DL collides with FL
    [TestCase(0x80000003UL, 3, 0U)] // Unsupported position
    [TestCase(3UL, 6, 0U)] // Inconsistent mask/count
    public void NativeMask_ReportsOnlyRepresentablePositions(ulong mask, int channels, uint expected)
    {
        var layout = new AVChannelLayout
        {
            order = AVChannelOrder.AV_CHANNEL_ORDER_NATIVE,
            nb_channels = channels,
            u = new AVChannelLayout_u { mask = mask }
        };
        Assert.That(FFmpegReader.GetChannelLayout(layout), Is.EqualTo(new AudioChannelLayout(channels, (AudioSpeakers)expected)));
        Assert.That(FFmpegReader.GetOutputLayout(layout).u.mask, Is.EqualTo(mask), "swr must keep the original native layout");
    }

    [Test]
    public void LayoutWithoutChannels_FallsBackToNativeStereo()
    {
        AVChannelLayout layout = FFmpegReader.GetOutputLayout(default);
        Assert.That(layout.order, Is.EqualTo(AVChannelOrder.AV_CHANNEL_ORDER_NATIVE));
        Assert.That(layout.nb_channels, Is.EqualTo(2));
        Assert.That(layout.u.mask, Is.EqualTo(ffmpeg.AV_CH_LAYOUT_STEREO));
        Assert.That(FFmpegReader.GetChannelLayout(layout), Is.EqualTo(AudioChannelLayout.Stereo));
    }

    [Test]
    public void AacSurroundFixture_ReturnsEachToneAtItsSpeakerPosition()
    {
        using var reader = Open("surround51-tones.m4a");
        Assert.That(reader.AudioInfo.NumChannels, Is.EqualTo(6));
        Assert.That(reader.AudioInfo.ChannelLayout, Is.EqualTo(AudioChannelLayout.Surround51));
        Assert.That(reader.ReadAudio(0, 0, out AudioBuffer? empty), Is.True);
        using (empty)
            Assert.That(empty!.ChannelCount, Is.EqualTo(6));

        const int start = 12000;
        const int length = 12000;
        Assert.That(reader.ReadAudio(start, length, out AudioBuffer? audio), Is.True);
        using (audio)
        {
            Assert.That(audio!.ChannelCount, Is.EqualTo(6));
            AssertTones(audio, reader.AudioInfo.ChannelLayout);
            byte[] packed = new byte[length * 6 * sizeof(float) + 16];
            Array.Fill(packed, (byte)0xCD);
            Assert.That(reader.ReadAudio(start, length, packed.AsSpan(0, packed.Length - 16), out AudioFrameInfo info), Is.True);
            Assert.That(info.NumChannels, Is.EqualTo(6));
            Assert.That(info.NumSamples, Is.EqualTo(length));
            Assert.That(info.DataLength, Is.EqualTo(length * 6 * sizeof(float)));
            Assert.That(packed[^16..], Is.All.EqualTo((byte)0xCD));
            using var raw = new AudioBuffer(info.SampleRate, info.NumChannels, info.NumSamples);
            AudioConversion.Deinterleave(MemoryMarshal.Cast<byte, float>(packed.AsSpan(0, info.DataLength)), raw);
            AssertTones(raw, reader.AudioInfo.ChannelLayout);
        }
    }

    [Test]
    public void MonoFixture_ReturnsOneChannel_AndUsesItsFrameSizeForSilence()
    {
        using var reader = Open("late-audio.mp4");
        Assert.That(reader.AudioInfo.NumChannels, Is.EqualTo(1));
        Assert.That(reader.AudioInfo.ChannelLayout, Is.EqualTo(AudioChannelLayout.Mono));
        Assert.That(reader.ReadAudio(8000, 256, out AudioBuffer? audio), Is.True);
        using (audio)
        {
            Assert.That(audio!.ChannelCount, Is.EqualTo(1));
            Assert.That(audio.GetChannelData(0).ToArray(), Has.Some.Not.EqualTo(0));
        }
        byte[] silence = new byte[256 * sizeof(float) + 16];
        Array.Fill(silence, (byte)0xCD);
        Assert.That(reader.ReadAudio(0, 256, silence.AsSpan(0, 256 * sizeof(float)), out AudioFrameInfo info), Is.True);
        Assert.That(info.NumChannels, Is.EqualTo(1));
        Assert.That(info.DataLength, Is.EqualTo(256 * sizeof(float)));
        Assert.That(silence[..info.DataLength], Is.All.Zero);
        Assert.That(silence[^16..], Is.All.EqualTo((byte)0xCD));
    }

    [Test]
    public void Handler_CarriesLayoutAndSizesSharedMemoryForSixChannels()
    {
        using var handler = new DecodingHandler();
        OpenFileResponse opened = handler.HandleOpen(IpcMessage.Create(1, MessageType.OpenFile, new OpenFileRequest
        {
            FilePath = Fixture("surround51-tones.m4a"),
            StreamsToLoad = (int)MediaMode.Audio
        })).GetPayload<OpenFileResponse>()!;
        Assert.That(opened.AudioNumChannels, Is.EqualTo(6));
        Assert.That(opened.AudioSpeakerMask, Is.EqualTo((uint)AudioChannelLayout.Surround51.Speakers));
        ReadAudioResponse Read(int length) => handler.HandleReadAudio(IpcMessage.Create(2, MessageType.ReadAudio,
            new ReadAudioRequest { ReaderId = opened.ReaderId, Start = 0, Length = length })).GetPayload<ReadAudioResponse>()!;

        ReadAudioResponse response = Read(opened.AudioSampleRate);
        Assert.That(response.Success, Is.True, "one second of six-channel audio must fit the buffer created at open");
        Assert.That(response.NumChannels, Is.EqualTo(6));
        Assert.That(response.NumSamples, Is.EqualTo(opened.AudioSampleRate));
        Assert.That(response.DataLength, Is.EqualTo(response.NumSamples * 6 * sizeof(float)));
        using var shared = SharedMemoryBuffer.Open(opened.AudioSharedMemoryName!, response.DataLength + 64);
        byte[] bytes = new byte[response.DataLength];
        shared.Read(bytes);
        using var audio = new AudioBuffer(response.SampleRate, 6, response.NumSamples);
        AudioConversion.Deinterleave(MemoryMarshal.Cast<byte, float>(bytes), audio);
        AssertTones(audio, new AudioChannelLayout(opened.AudioNumChannels, (AudioSpeakers)opened.AudioSpeakerMask));
        Assert.That(Read(opened.AudioSampleRate + 1).Success, Is.False, "the per-read capacity check must also use six channels");
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static FFmpegReader Open(string name) => new(Fixture(name), new MediaOptions(MediaMode.Audio), new FFmpegDecodingSettings());

    private static AVChannelLayout Layout(int channels, bool positions = true) => new()
    {
        order = positions ? AVChannelOrder.AV_CHANNEL_ORDER_NATIVE : AVChannelOrder.AV_CHANNEL_ORDER_UNSPEC,
        nb_channels = channels,
        u = new AVChannelLayout_u { mask = positions ? (uint)AudioChannelLayout.GetDefault(channels).Speakers : 0 }
    };

    private static unsafe AVChannelLayout CustomLayout(string order)
    {
        AVChannelLayout layout = default;
        ffmpeg.av_channel_layout_custom_init(&layout, 6).ThrowIfError();
        for (int i = 0; i < 6; i++)
            layout.u.map[i].id = order == "unknown" ? AVChannel.AV_CHAN_UNKNOWN : (AVChannel)i;
        if (order == "reversed")
            (layout.u.map[0].id, layout.u.map[1].id) = (layout.u.map[1].id, layout.u.map[0].id);
        return layout;
    }

    // Transfers ownership of any CUSTOM map to this frame, exactly as an FFmpeg decoder does.
    private static unsafe MediaFrame Frame(AVChannelLayout layout, int samples, int rate, float[] markers)
    {
        var frame = new MediaFrame
        {
            ChLayout = layout,
            SampleRate = rate,
            Format = (int)AVSampleFormat.AV_SAMPLE_FMT_FLTP,
            NbSamples = samples
        };
        frame.AllocateBuffer();
        AVFrame* native = frame;
        for (int c = 0; c < markers.Length; c++)
            for (int i = 0; i < samples; i++)
                ((float*)native->extended_data[c])[i] = markers[c] + i / 16384f;
        return frame;
    }

    private static unsafe void AssertSamples(MediaFrame output, int samples, int rate, float[] markers)
    {
        Assert.That(output.NbSamples, Is.EqualTo(samples));
        Assert.That(output.SampleRate, Is.EqualTo(rate));
        Assert.That(output.Format, Is.EqualTo((int)AVSampleFormat.AV_SAMPLE_FMT_FLT));
        Assert.That(output.ChLayout.nb_channels, Is.EqualTo(markers.Length));
        for (int c = 0; c < markers.Length; c++)
            Assert.That(Enumerable.Range(0, samples).Select(i => ((float*)output.Data[0])[i * markers.Length + c]).ToArray(),
                Is.EqualTo(Enumerable.Range(0, samples).Select(i => markers[c] == 0 ? 0 : markers[c] + i / 16384f).ToArray()));
    }

    private static void AssertTones(AudioBuffer audio, AudioChannelLayout layout)
    {
        var frequencies = new Dictionary<AudioSpeakers, int>
        {
            [AudioSpeakers.FrontLeft] = 400,
            [AudioSpeakers.FrontRight] = 600,
            [AudioSpeakers.FrontCenter] = 800,
            [AudioSpeakers.LowFrequency] = 80,
            [AudioSpeakers.BackLeft] = 1000,
            [AudioSpeakers.BackRight] = 1200
        };
        for (int c = 0; c < audio.ChannelCount; c++)
        {
            double Power(int frequency)
            {
                double coefficient = 2 * Math.Cos(2 * Math.PI * frequency / audio.SampleRate);
                double s1 = 0, s2 = 0;
                foreach (float sample in audio.GetChannelData(c))
                {
                    double s0 = sample + coefficient * s1 - s2;
                    s2 = s1;
                    s1 = s0;
                }
                return s1 * s1 + s2 * s2 - coefficient * s1 * s2;
            }
            int expected = frequencies[layout.GetSpeaker(c)];
            var powers = frequencies.Values.ToDictionary(f => f, Power);
            Assert.That(powers.MaxBy(p => p.Value).Key, Is.EqualTo(expected), $"channel {c}: {layout.GetSpeaker(c)}");
            Assert.That(powers[expected], Is.GreaterThan(16 * powers.Where(p => p.Key != expected).Max(p => p.Value)));
        }
    }
}
