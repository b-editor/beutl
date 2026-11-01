using Beutl.Audio;
using Beutl.Testing.Headless;
using Silk.NET.OpenAL;
using AudioContext = Beutl.Audio.Platforms.OpenAL.AudioContext;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
[NonParallelizable] // OpenAL's current context is process-wide.
public unsafe class OpenALLoopbackTests
{
    private const int SampleRate = 48000;
    private const int WarmupSamples = SampleRate / 10;
    private const float ToneAmplitude = 0.1f;
    private const int FloatSoft = 0x1406;
    private static readonly int[] s_frequencies = [401, 601, 809, 101, 1009, 1201, 1409, 1601];

    [OneTimeSetUp]
    public void LoadOpenALSoft() => OpenALPreload.EnsureLoaded();

    [TestCase(2, 1)]
    [TestCase(2, 2)]
    [TestCase(2, 6)]
    [TestCase(2, 8)]
    [TestCase(6, 1)]
    [TestCase(6, 2)]
    [TestCase(6, 6)]
    [TestCase(8, 1)]
    [TestCase(8, 2)]
    [TestCase(8, 6)]
    [TestCase(8, 8)]
    public void Playback_RoutesEachProjectSpeakerToItsOutput(int deviceChannels, int projectChannels)
    {
        using var alc = ALContext.GetApi(soft: true);
        using var al = AL.GetApi(soft: true);
        Assert.That(alc.IsExtensionPresent(null, "ALC_SOFT_loopback"), Is.True);

        // Silk.NET has no loopback binding. ALCboolean is a single byte.
        var openDevice = (delegate* unmanaged[Cdecl]<byte*, Device*>)GetProcAddress(alc, "alcLoopbackOpenDeviceSOFT");
        var supportsFormat = (delegate* unmanaged[Cdecl]<Device*, int, int, int, byte>)GetProcAddress(alc, "alcIsRenderFormatSupportedSOFT");
        var renderSamples = (delegate* unmanaged[Cdecl]<Device*, float*, int, void>)GetProcAddress(alc, "alcRenderSamplesSOFT");
        Device* device = openDevice(null);
        Assert.That((nint)device, Is.Not.EqualTo(nint.Zero));

        AudioContext? context = null;
        try
        {
            int channelsFormat = deviceChannels switch
            {
                6 => 0x1504, // ALC_5POINT1_SOFT
                8 => 0x1506, // ALC_7POINT1_SOFT
                _ => 0x1501 // ALC_STEREO_SOFT
            };
            Assert.That(supportsFormat(device, SampleRate, channelsFormat, FloatSoft), Is.EqualTo(1));
            context = new AudioContext(soft: true, device,
            [
                0x1007, SampleRate, // ALC_FREQUENCY
                0x1990, channelsFormat, // ALC_FORMAT_CHANNELS_SOFT
                0x1991, FloatSoft, // ALC_FORMAT_TYPE_SOFT
                0x1992, 0, // ALC_HRTF_SOFT: disable headphone processing
                0x199A, 0, // ALC_OUTPUT_LIMITER_SOFT: measure unmodified gains
                0
            ]);

            Assert.That(al.IsExtensionPresent("AL_SOFT_direct_channels"), Is.True);
            Assert.That(al.IsExtensionPresent("AL_SOFT_direct_channels_remix"), Is.True);
            Assert.That(alc.IsExtensionPresent(device, "ALC_SOFT_output_mode"), Is.True);
            int outputMode;
            alc.GetContextProperty(device, (GetContextInteger)0x19AC, 1, &outputMode); // ALC_OUTPUT_MODE_SOFT
            context.VerifyError();
            TestContext.Out.WriteLine($"{deviceChannels}-channel loopback: ALC_OUTPUT_MODE_SOFT = 0x{outputMode:X}");
            AudioChannelLayout expectedDeviceLayout = AudioChannelLayout.GetDefault(deviceChannels);
            Assert.That(AudioContext.GetDeviceLayout(outputMode, supportsMultichannel: true), Is.EqualTo(expectedDeviceLayout));
            Assert.That(context.DeviceLayout, Is.EqualTo(expectedDeviceLayout));

            AudioChannelLayout project = AudioChannelLayout.GetDefault(projectChannels);
            var (outputLayout, matrix) = AudioPlaybackOutput.Create(project, context.DeviceLayout);
            using var audio = AudioTestBuffers.CreateBuffer(projectChannels, WarmupSamples + SampleRate,
                (channel, sample) => ToneAmplitude * (float)Math.Sin(2 * Math.PI * s_frequencies[channel] * sample / SampleRate),
                SampleRate);
            using AudioBuffer? converted = matrix?.Apply(audio);

            uint buffer = context.GenBuffer();
            uint source = 0;
            try
            {
                // Follow PlayerViewModel: mono/stereo projects use the stereo overload.
                if (AudioPlaybackOutput.UseMultichannelBackend(project))
                    context.BufferData(buffer, converted ?? audio, outputLayout);
                else
                    context.BufferData(buffer, converted ?? audio);

                source = context.GenSource();
                context.GetSource(source, (GetSourceInteger)0x1033, out int directChannelsMode);
                Assert.That(directChannelsMode, Is.EqualTo(2), "AL_REMIX_UNMATCHED_SOFT");
                context.SourceQueueBuffer(source, buffer);
                context.SourcePlay(source);

                var rendered = new float[(WarmupSamples + SampleRate) * deviceChannels];
                fixed (float* samples = rendered)
                {
                    renderSamples(device, samples, WarmupSamples + SampleRate);
                }
                context.VerifyError();

                // OpenAL's 5.1 render order uses side speakers (mask 0x60F), while Beutl
                // labels the last pair BL/BR. AL_FORMAT_51CHN16 maps that pair to SL/SR.
                AudioChannelLayout renderLayout = deviceChannels == 6
                    ? new AudioChannelLayout(6, (AudioSpeakers)0x60F)
                    : expectedDeviceLayout;
                using (Assert.EnterMultipleScope())
                {
                    for (int input = 0; input < projectChannels; input++)
                    {
                        for (int output = 0; output < deviceChannels; output++)
                        {
                            double gain = MeasureGain(rendered, deviceChannels, output, s_frequencies[input]);
                            double expected = ExpectedGain(project, renderLayout, input, output);
                            Assert.That(gain, Is.EqualTo(expected).Within(0.002),
                                $"{project.GetSpeaker(input)} -> {renderLayout.GetSpeaker(output)}");
                        }
                    }
                }
            }
            finally
            {
                if (source != 0)
                    context.DeleteSource(source);
                context.DeleteBuffer(buffer);
            }
        }
        finally
        {
            if (context != null)
                context.Dispose(); // Also closes the loopback device.
            else
                alc.CloseDevice(device);
        }
    }

    private static void* GetProcAddress(ALContext alc, string name)
    {
        void* address = alc.GetProcAddress(null, name);
        Assert.That((nint)address, Is.Not.EqualTo(nint.Zero), name);
        return address;
    }

    private static double ExpectedGain(AudioChannelLayout project, AudioChannelLayout render, int input, int output)
    {
        AudioSpeakers from = project.GetSpeaker(input);
        AudioSpeakers to = render.GetSpeaker(output);
        if (project == AudioChannelLayout.Mono)
            return to is AudioSpeakers.FrontLeft or AudioSpeakers.FrontRight ? 1 : 0;

        if (render == AudioChannelLayout.Stereo)
        {
            // Independent expected coefficients for Beutl's stereo fold; never derive
            // the oracle from AudioChannelMatrix, which is part of the path under test.
            return (from, to) switch
            {
                (AudioSpeakers.FrontLeft, AudioSpeakers.FrontLeft) => 1,
                (AudioSpeakers.FrontRight, AudioSpeakers.FrontRight) => 1,
                (AudioSpeakers.FrontCenter, _) => Math.Sqrt(0.5),
                (AudioSpeakers.BackLeft or AudioSpeakers.SideLeft, AudioSpeakers.FrontLeft) => Math.Sqrt(0.5),
                (AudioSpeakers.BackRight or AudioSpeakers.SideRight, AudioSpeakers.FrontRight) => Math.Sqrt(0.5),
                _ => 0 // Includes LFE, which Beutl deliberately drops when folding.
            };
        }

        if (project == AudioChannelLayout.Surround51)
        {
            from = from switch
            {
                AudioSpeakers.BackLeft => AudioSpeakers.SideLeft,
                AudioSpeakers.BackRight => AudioSpeakers.SideRight,
                _ => from
            };
        }
        return from == to ? 1 : 0;
    }

    private static double MeasureGain(float[] interleaved, int channels, int channel, int frequency)
    {
        // Goertzel over one whole second: integer-Hz tones occupy distinct bins.
        // Skip the initial gain ramp, and tolerate only 16-bit upload quantization.
        double coefficient = 2 * Math.Cos(2 * Math.PI * frequency / SampleRate);
        double previous = 0;
        double beforePrevious = 0;
        for (int sample = WarmupSamples; sample < WarmupSamples + SampleRate; sample++)
        {
            double current = interleaved[sample * channels + channel] + coefficient * previous - beforePrevious;
            beforePrevious = previous;
            previous = current;
        }
        double power = previous * previous + beforePrevious * beforePrevious - coefficient * previous * beforePrevious;
        return 2 * Math.Sqrt(Math.Max(0, power)) / SampleRate / ToneAmplitude;
    }
}
