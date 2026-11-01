using Beutl.Audio;
using Beutl.FFmpegWorker.Encoding;
using Beutl.Media;
using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;

namespace Beutl.FFmpegWorker.Tests;

[TestFixture]
public class EncodingCancellationTests
{
    [Test]
    public void RedactPath_ReplacesBeutlHomePrefix()
    {
        string home = BeutlEnvironment.GetHomeDirectoryPath();
        string path = Path.Combine(home, "ffmpeg", "native");

        Assert.That(FFmpegLoaderWorker.RedactPath(path), Is.EqualTo("~" + path[home.Length..]));
    }

    [Test]
    public void RedactPath_ReplacesUserProfilePrefix()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assume.That(profile, Is.Not.Null.And.Not.Empty);
        string path = Path.Combine(profile, "private", "ffmpeg");

        Assert.That(FFmpegLoaderWorker.RedactPath(path), Is.EqualTo("~" + path[profile.Length..]));
    }

    private string _workDir = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "beutl-ffmpeg-cancel-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_workDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private static void RequireFFmpegWithMp4Encoders()
    {
        FFmpegTestHelpers.RequireFFmpeg();

        // Loading the libraries is not enough: the .mp4 cancellation path drives
        // MediaCodec.FindEncoder for the container's default video+audio codecs, so a build that
        // loads the libs but lacks those encoders would fail during stream setup instead of
        // exercising cancellation. Require the same codecs GuessFormat(".mp4") selects.
        OutputFormat outFormat = OutputFormat.GuessFormat(null, "probe.mp4", null);
        bool hasEncoders = (outFormat.VideoCodec == AVCodecID.AV_CODEC_ID_NONE
                            || MediaCodec.FindEncoder(outFormat.VideoCodec) != null)
                           && (outFormat.AudioCodec == AVCodecID.AV_CODEC_ID_NONE
                               || MediaCodec.FindEncoder(outFormat.AudioCodec) != null);
        if (!hasEncoders)
            Assert.Ignore("FFmpeg native libraries are not available.");
    }

    // A cancelled Encode must throw OperationCanceledException. FFmpeg runs wherever its native
    // libraries are present, so this test self-skips when the native path is unavailable.
    [Test]
    public void EncodeSurfacesCancellationAsOperationCanceledException()
    {
        RequireFFmpegWithMp4Encoders();

        string outputPath = Path.Combine(_workDir, "cancelled.mp4");

        const int width = 64;
        const int height = 64;
        const int sampleRate = 44100;
        const int frameCount = 30;
        const int frameRateNum = 30;
        const int frameRateDen = 1;

        var controller = FFmpegTestHelpers.CreateController(
            outputPath, new PixelSize(width, height), new Rational(frameRateNum, frameRateDen), sampleRate, channels: 2);

        using var cts = new CancellationTokenSource();
        // Cancel mid-clip (after one frame) so the controller exits its loop on cancellation and
        // reaches the ThrowIfCancellationRequested() guard rather than completing the encode.
        var frameProvider = new CancelAfterFirstFrameProvider(
            cts, frameCount, new Rational(frameRateNum, frameRateDen), width, height);
        var sampleProvider = new SineSampleProvider(sampleRate, sampleRate);

        Assert.That(
            async () => await controller.Encode(frameProvider, sampleProvider, cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task A_single_frame_video_keeps_its_frame_duration()
    {
        RequireFFmpegWithMp4Encoders();

        string outputPath = Path.Combine(_workDir, "single-frame.mp4");
        var controller = FFmpegTestHelpers.CreateController(
            outputPath, new PixelSize(32, 24), new Rational(1, 1), sampleRate: 44100, channels: 2);
        using var frames = new GradientFrameProvider(1, new Rational(1, 1), 32, 24);
        using var samples = new SineSampleProvider(44100, 44100);

        await controller.Encode(frames, samples, CancellationToken.None);

        using MediaDemuxer demuxer = MediaDemuxer.Open(outputPath);
        MediaStream video = demuxer.Single(s => s.CodecparRef.codec_type == AVMediaType.AVMEDIA_TYPE_VIDEO);
        // One frame at 1 fps lasts one second; a zero-length sample leaves nothing for a decoder to show.
        // Half a frame of tolerance leaves room for the container's timescale.
        Assert.That(video.Duration * ffmpeg.av_q2d(video.TimeBase), Is.EqualTo(1).Within(0.5));
    }

    [Test]
    public async Task EncodingOverALargerFile_DoesNotRetainThePreviousTail()
    {
        RequireFFmpegWithMp4Encoders();

        string outputPath = Path.Combine(_workDir, "overwrite.mp4");
        byte[] previous = Enumerable.Repeat((byte)0x5a, 1024 * 1024).ToArray();
        File.WriteAllBytes(outputPath, previous);
        var controller = FFmpegTestHelpers.CreateController(
            outputPath, new PixelSize(64, 64), new Rational(30, 1), sampleRate: 44100, channels: 2);
        using var frames = new GradientFrameProvider(3, new Rational(30, 1), 64, 64);
        using var samples = new SineSampleProvider(4410, 44100);

        await controller.Encode(frames, samples, CancellationToken.None);

        Assert.That(new FileInfo(outputPath).Length, Is.InRange(1, previous.Length - 1));
    }

    // Direct callers can still supply stereo for a different encoder channel count.
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(6)]
    public async Task Encode_WritesTheConfiguredAudioChannelCount(int channels)
    {
        RequireFFmpegWithMp4Encoders();

        string outputPath = Path.Combine(_workDir, $"channels-{channels}.mp4");
        var controller = FFmpegTestHelpers.CreateController(
            outputPath, new PixelSize(64, 64), new Rational(30, 1), sampleRate: 44100, channels);
        using var frames = new GradientFrameProvider(3, new Rational(30, 1), 64, 64);
        // A quieter right side tells the channels apart, so a swapped or dropped one changes the levels.
        using var samples = new SineSampleProvider(4410, 44100, rightGain: 0.25f);

        await controller.Encode(frames, samples, CancellationToken.None);

        using MediaDemuxer demuxer = MediaDemuxer.Open(outputPath);
        MediaStream audio = demuxer.Single(s => s.CodecparRef.codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO);
        Assert.That(audio.CodecparRef.ch_layout.nb_channels, Is.EqualTo(channels));

        Dictionary<AVChannel, double> levels = MeasureChannelLevels(demuxer, audio);
        if (channels == 1)
        {
            Assert.That(levels[AVChannel.AV_CHAN_FRONT_CENTER], Is.GreaterThan(0.05));
        }
        else
        {
            Assert.That(levels[AVChannel.AV_CHAN_FRONT_RIGHT], Is.GreaterThan(0.01));
            Assert.That(levels[AVChannel.AV_CHAN_FRONT_LEFT] / levels[AVChannel.AV_CHAN_FRONT_RIGHT], Is.InRange(3.0, 5.0));
        }
    }

    [TestCase(6, "m4a")]
    [TestCase(8, "m4a")]
    [TestCase(3, "wav")]
    [TestCase(4, "wav")]
    [TestCase(6, "wav")]
    [TestCase(8, "wav")]
    [TestCase(12, "wav")]
    public async Task Encode_PreservesEachSpeakerTone(int channels, string extension)
    {
        FFmpegTestHelpers.RequireFFmpeg();
        string path = Path.Combine(_workDir, $"speakers-{channels}.{extension}");
        const int rate = 48000;
        var layout = AudioChannelLayout.GetDefault(channels);
        var codec = MediaCodec.FindEncoder(extension == "m4a" ? "aac" : "pcm_f32le");
        if (codec == null || (extension == "m4a" && codec.GetChLayouts().Any()
            && !codec.GetChLayouts().Any(l => l.nb_channels == channels)))
            Assert.Ignore($"This FFmpeg build cannot encode {channels} channels as {extension}.");
        var controller = FFmpegTestHelpers.CreateController(path, new PixelSize(32, 32), new Rational(30), rate, channels);
        controller.AudioSettings.Codec.Value = new CodecRecord(extension == "m4a" ? "aac" : "pcm_f32le", "test");
        controller.AudioSettings.Format.Value = FFmpegAudioEncoderSettings.AudioFormat.Default;
        controller.AudioSettings.Bitrate.Value = channels * 96000;
        using var frames = new GradientFrameProvider(0, new Rational(30), 32, 32);
        using var samples = new SpeakerToneSampleProvider(rate * 2, rate, layout);
        await controller.Encode(frames, samples, CancellationToken.None);

        using var demuxer = MediaDemuxer.Open(path);
        var stream = demuxer.Single(s => s.CodecparRef.codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO);
        Assert.That(stream.CodecparRef.ch_layout.nb_channels, Is.EqualTo(channels));
        if (layout.HasPositions)
            Assert.That(stream.CodecparRef.ch_layout.u.mask, Is.EqualTo((ulong)layout.Speakers));
        float[][] decoded = DecodeChannels(demuxer, stream);
        for (int ch = 0; ch < channels; ch++)
        {
            // Use the steady middle second so AAC priming and padding do not weaken the channel checks.
            var middle = decoded[ch].AsSpan(rate / 2, rate);
            for (int tone = 0; tone < channels; tone++)
            {
                double level = SpeakerToneSampleProvider.ToneLevel(middle, SpeakerToneSampleProvider.Frequency(tone), rate);
                Assert.That(level, tone == ch ? Is.InRange(0.18, 0.30) : Is.LessThan(0.025),
                    $"Speaker {ch}, tone {tone}, {extension}: {level}");
            }
        }
    }

    [TestCase(2, 384064L)]
    [TestCase(6, 1152064L)]
    [TestCase(12, 2304064L)]
    public void SharedAudioBuffer_HoldsOneSecondOfProviderChannels(int channels, long expectedBytes)
    {
        var request = new Beutl.FFmpegIpc.Protocol.Messages.EncodeStartRequest
        {
            ProviderSampleRate = 48000,
            ProviderChannelCount = channels,
            AudioChannels = 1,
        };
        Assert.That(Beutl.FFmpegWorker.Handlers.EncodingHandler.GetAudioBufferSize(request), Is.EqualTo(expectedBytes));
    }

    [TestCase(6, 0x60fu)]
    [TestCase(8, 0xffu)]
    public void SelectChannelLayout_PrefersTheSpeakerMaskOverTheFirstMatchingCount(int channels, uint otherMask)
    {
        FFmpegTestHelpers.RequireFFmpeg();
        var requested = AudioChannelLayout.GetDefault(channels);
        AVChannelLayout Native(uint mask) => new()
        {
            order = AVChannelOrder.AV_CHANNEL_ORDER_NATIVE,
            nb_channels = channels,
            u = new AVChannelLayout_u { mask = mask },
        };
        var other = Native(otherMask);
        var exact = Native((uint)requested.Speakers);
        Assert.That(FFmpegEncodingController.SelectChannelLayout([other, exact], requested).u.mask,
            Is.EqualTo(exact.u.mask));
        Assert.That(FFmpegEncodingController.SelectChannelLayout([other], requested).u.mask,
            Is.EqualTo(other.u.mask), "Fall back to a supported layout with the requested count.");
        Assert.That(FFmpegEncodingController.SelectChannelLayout([], requested).u.mask,
            Is.EqualTo(exact.u.mask), "Unrestricted codecs retain Beutl's native speaker order.");
    }

    [TestCase(3, 0x7u)]
    [TestCase(4, 0x33u)]
    [TestCase(3, 0u)]
    public void SelectChannelLayout_WithoutRestrictionsPreservesTheRequestedLayout(int channels, uint mask)
    {
        var requested = new AudioChannelLayout(channels, (AudioSpeakers)mask);
        var actual = FFmpegEncodingController.SelectChannelLayout([], requested);
        Assert.Multiple(() =>
        {
            Assert.That(actual.nb_channels, Is.EqualTo(channels));
            Assert.That(actual.order, Is.EqualTo(mask == 0
                ? AVChannelOrder.AV_CHANNEL_ORDER_UNSPEC : AVChannelOrder.AV_CHANNEL_ORDER_NATIVE));
            Assert.That(actual.u.mask, Is.EqualTo((ulong)mask));
        });
    }

    // CoreAudio reads AAC channel configuration 7 as 7.1 (wide), which put FL/FR on Lc/Rc and the side pair on L/R (#3798).
    [Test]
    public async Task Encode_Aac71_CoreAudioPlaysEachToneFromItsSpeaker()
    {
        FFmpegTestHelpers.RequireFFmpeg();
        if (!OperatingSystem.IsMacOS() || !File.Exists("/usr/bin/afconvert"))
            Assert.Ignore("CoreAudio's afconvert is only available on macOS.");
        string path = await EncodeAac71("speakers-71.m4a");

        // MPEG_7_1_C is L R C LFE Ls Rs Rls Rrs. Beutl's 7.1 is FL FR FC LFE BL BR SL SR, and Ls/Rs are its sides.
        string wav = Path.Combine(_workDir, "speakers-71-coreaudio.wav");
        RunAfconvert("-f", "WAVE", "-d", "LEF32", "-l", "MPEG_7_1_C", path, wav);
        int[] toneOfSpeaker = [0, 1, 2, 3, 6, 7, 4, 5];
        const int rate = 48000;
        using var demuxer = MediaDemuxer.Open(wav);
        var stream = demuxer.Single(s => s.CodecparRef.codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO);
        float[][] decoded = DecodeChannels(demuxer, stream);
        Assert.That(decoded, Has.Length.EqualTo(8));
        Assert.Multiple(() =>
        {
            for (int speaker = 0; speaker < 8; speaker++)
            {
                var middle = decoded[speaker].AsSpan(rate / 2, rate);
                for (int tone = 0; tone < 8; tone++)
                {
                    double level = SpeakerToneSampleProvider.ToneLevel(middle, SpeakerToneSampleProvider.Frequency(tone), rate);
                    Assert.That(level, tone == toneOfSpeaker[speaker] ? Is.InRange(0.18, 0.30) : Is.LessThan(0.025),
                        $"CoreAudio speaker {speaker}, tone {tone}: {level}");
                }
            }
        });
    }

    [Test]
    public async Task Encode_Aac71_TagsChannelConfiguration12()
    {
        FFmpegTestHelpers.RequireFFmpeg();
        string path = await EncodeAac71("config-71.m4a");

        using var demuxer = MediaDemuxer.Open(path);
        var stream = demuxer.Single(s => s.CodecparRef.codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO);
        byte[] asc = GetExtradata(stream);
        // audioObjectType 2 (LC) and samplingFrequencyIndex 3 (48 kHz) put channelConfiguration at bits 9-12.
        Assert.That(asc, Has.Length.GreaterThanOrEqualTo(2));
        Assert.That(((asc[0] << 8 | asc[1]) >> 3) & 0xf, Is.EqualTo(12));
    }

    [Test]
    public async Task Encode_Aac71ToAdts_FailsWithTheReason()
    {
        FFmpegTestHelpers.RequireFFmpeg();
        if (MediaCodec.FindEncoder("aac") == null)
            Assert.Ignore("This FFmpeg build has no AAC encoder.");
        string path = Path.Combine(_workDir, "speakers-71.aac");
        var controller = CreateAac71Controller(path);
        using var frames = new GradientFrameProvider(0, new Rational(30), 32, 32);
        using var samples = new SpeakerToneSampleProvider(48000, 48000, AudioChannelLayout.GetDefault(8));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await controller.Encode(frames, samples, CancellationToken.None));
        Assert.That(exception!.Message, Does.StartWith("7.1 AAC cannot be written to adts"));
    }

    [TestCase("11b856e500", "11e056e500", true)] // FFmpeg 8, 7.1 at 48 kHz
    [TestCase("11e056e500", "11e056e500", false)] // FFmpeg 9 already writes 12
    [TestCase("11b056e500", "11b056e500", false)] // 5.1
    [TestCase("1780562230", "1780562230", false)] // Explicit 44100 Hz (index 15), 5.1
    [TestCase("1780562238", "1780562260", true)] // Explicit 44100 Hz, 7.1
    [TestCase("11", "11", false)]
    public void RetagSevenPointOne_RewritesOnlyConfiguration7(string before, string after, bool retagged)
    {
        byte[] asc = Convert.FromHexString(before);
        Assert.Multiple(() =>
        {
            Assert.That(FFmpegEncodingController.RetagSevenPointOne(asc), Is.EqualTo(retagged));
            Assert.That(Convert.ToHexString(asc).ToLowerInvariant(), Is.EqualTo(after));
        });
    }

    private FFmpegEncodingController CreateAac71Controller(string path)
    {
        var controller = FFmpegTestHelpers.CreateController(path, new PixelSize(32, 32), new Rational(30), 48000, 8);
        controller.AudioSettings.Codec.Value = new CodecRecord("aac", "test");
        controller.AudioSettings.Format.Value = FFmpegAudioEncoderSettings.AudioFormat.Default;
        controller.AudioSettings.Bitrate.Value = 8 * 96000;
        return controller;
    }

    private async Task<string> EncodeAac71(string fileName)
    {
        var codec = MediaCodec.FindEncoder("aac");
        if (codec == null || (codec.GetChLayouts().Any() && !codec.GetChLayouts().Any(l => l.nb_channels == 8)))
            Assert.Ignore("This FFmpeg build cannot encode 8 channels as AAC.");
        string path = Path.Combine(_workDir, fileName);
        var controller = CreateAac71Controller(path);
        using var frames = new GradientFrameProvider(0, new Rational(30), 32, 32);
        using var samples = new SpeakerToneSampleProvider(48000 * 2, 48000, AudioChannelLayout.GetDefault(8));
        await controller.Encode(frames, samples, CancellationToken.None);
        return path;
    }

    private static unsafe byte[] GetExtradata(MediaStream stream) =>
        new ReadOnlySpan<byte>(stream.CodecparRef.extradata, stream.CodecparRef.extradata_size).ToArray();

    private static void RunAfconvert(params string[] arguments)
    {
        var info = new System.Diagnostics.ProcessStartInfo("/usr/bin/afconvert")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
            info.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(info)!;
        Task<string> error = process.StandardError.ReadToEndAsync();
        _ = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(30000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("afconvert timed out.");
        }
        Assert.That(process.ExitCode, Is.Zero, error.Result);
    }

    private static unsafe float[][] DecodeChannels(MediaDemuxer demuxer, MediaStream stream)
    {
        var layout = stream.CodecparRef.ch_layout;
        var channels = Enumerable.Range(0, layout.nb_channels).Select(_ => new List<float>()).ToArray();
        using var decoder = MediaDecoder.CreateDecoder(stream.CodecparRef);
        using var frame = new MediaFrame();
        using var converter = SampleConverter.Create(layout, stream.CodecparRef.sample_rate, AVSampleFormat.AV_SAMPLE_FMT_FLT, 10000);
        foreach (var packet in demuxer.ReadPackets())
            if (packet.StreamIndex == stream.Index)
                Append(decoder.DecodePacket(packet, frame));
        Append(decoder.DecodePacket(null, frame));
        return channels.Select(channel => channel.ToArray()).ToArray();

        void Append(IEnumerable<MediaFrame> decoded)
        {
            foreach (var f in decoded)
            {
                using var packed = converter.ConvertFrame(f, out _, out _);
                var data = new ReadOnlySpan<float>((void*)packed.Data[0], packed.NbSamples * channels.Length);
                for (int i = 0; i < packed.NbSamples; i++)
                    for (int ch = 0; ch < channels.Length; ch++)
                        channels[ch].Add(data[i * channels.Length + ch]);
            }
        }
    }

    // The audio format editor switches to Auto when the codec lacks the current format. The default
    // encoders for .wav (pcm_s16le) and .webm (libopus) lack fltp, so both are exported with Auto.
    [TestCase("auto.wav")]
    [TestCase("auto.webm")]
    public async Task Encode_WithTheAutoAudioFormat_WritesTheAudio(string fileName)
    {
        RequireFFmpegWithMp4Encoders();

        string outputPath = Path.Combine(_workDir, fileName);
        OutputFormat outFormat = OutputFormat.GuessFormat(null, outputPath, null);
        if (MediaCodec.FindEncoder(outFormat.AudioCodec) == null
            || (outFormat.VideoCodec != AVCodecID.AV_CODEC_ID_NONE && MediaCodec.FindEncoder(outFormat.VideoCodec) == null))
        {
            Assert.Ignore($"This FFmpeg build has no default encoders for {Path.GetExtension(fileName)}.");
        }

        // libopus takes 48 kHz but not 44.1 kHz
        const int sampleRate = 48000;
        const int sampleCount = 4800;
        var controller = FFmpegTestHelpers.CreateController(
            outputPath, new PixelSize(64, 64), new Rational(30, 1), sampleRate, channels: 2);
        controller.AudioSettings.Format.Value = FFmpegAudioEncoderSettings.AudioFormat.Default;
        using var frames = new GradientFrameProvider(3, new Rational(30, 1), 64, 64);
        using var samples = new SineSampleProvider(sampleCount, sampleRate);

        await controller.Encode(frames, samples, CancellationToken.None);

        using MediaDemuxer demuxer = MediaDemuxer.Open(outputPath);
        MediaStream audio = demuxer.Single(s => s.CodecparRef.codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO);
        // Encoders pad the last frame, and PCM is written in 10,000-sample frames
        Assert.That(CountDecodedSamples(demuxer, audio), Is.GreaterThanOrEqualTo(sampleCount * 9 / 10));
    }

    private static long CountDecodedSamples(MediaDemuxer demuxer, MediaStream stream)
    {
        long count = 0;
        using MediaDecoder decoder = MediaDecoder.CreateDecoder(stream.CodecparRef);
        using var frame = new MediaFrame();
        foreach (MediaPacket packet in demuxer.ReadPackets())
        {
            if (packet.StreamIndex == stream.Index)
                count += decoder.DecodePacket(packet, frame).Sum(f => (long)f.NbSamples);
        }

        // A null packet drains the frames the decoder still holds.
        return count + decoder.DecodePacket(null, frame).Sum(f => (long)f.NbSamples);
    }

    // Decodes the audio stream and returns the RMS level of each channel, keyed by speaker position.
    private static unsafe Dictionary<AVChannel, double> MeasureChannelLevels(MediaDemuxer demuxer, MediaStream stream)
    {
        AVChannelLayout layout = stream.CodecparRef.ch_layout;
        var sumsOfSquares = new double[layout.nb_channels];
        long sampleCount = 0;
        using MediaDecoder decoder = MediaDecoder.CreateDecoder(stream.CodecparRef);
        using var frame = new MediaFrame();

        foreach (MediaPacket packet in demuxer.ReadPackets())
        {
            if (packet.StreamIndex == stream.Index)
                Accumulate(decoder.DecodePacket(packet, frame));
        }

        // A null packet drains the frames the decoder still holds.
        Accumulate(decoder.DecodePacket(null, frame));

        var levels = new Dictionary<AVChannel, double>();
        for (int ch = 0; ch < sumsOfSquares.Length; ch++)
        {
            levels[ffmpeg.av_channel_layout_channel_from_index(&layout, (uint)ch)] = Math.Sqrt(sumsOfSquares[ch] / sampleCount);
        }

        return levels;

        void Accumulate(IEnumerable<MediaFrame> decoded)
        {
            foreach (MediaFrame f in decoded)
            {
                Assert.That((AVSampleFormat)f.Format, Is.EqualTo(AVSampleFormat.AV_SAMPLE_FMT_FLTP));
                for (int ch = 0; ch < sumsOfSquares.Length; ch++)
                {
                    float* plane = (float*)f.Data[(uint)ch];
                    for (int i = 0; i < f.NbSamples; i++)
                        sumsOfSquares[ch] += plane[i] * plane[i];
                }

                sampleCount += f.NbSamples;
            }
        }
    }
}
