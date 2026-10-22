using System.Reflection;

using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg;
using Beutl.Extensions.FFmpeg.Decoding;
using Beutl.Extensions.FFmpeg.Proxy;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
[NonParallelizable]
public sealed class FFmpegDecoderInfoTests
{
    [TestCase("video.m4v")]
    [TestCase("VIDEO.M4V")]
    [TestCase("video.asf")]
    [TestCase("video.3gp")]
    [TestCase("video.3g2")]
    [TestCase("video.3gp2")]
    [TestCase("video.3gpp")]
    [TestCase("audio.adts")]
    public void GuessDecoder_WindowsMediaFormats_IncludeFFmpegFallback(string file)
    {
        IDecoderInfo decoder = new FFmpegDecoderInfo(new FFmpegDecodingSettings());
        DecoderRegistry.Register(decoder);
        try
        {
            Assert.That(DecoderRegistry.GuessDecoder(file), Does.Contain(decoder),
                "FFmpeg must remain a candidate when Media Foundation rejects a supported file extension.");
        }
        finally
        {
            DecoderRegistry.Unregister(decoder);
        }
    }

    // PackageManager.LoadExtension only instantiates [Export]-marked types, so without the attribute
    // an external FFmpeg package registers decoders/encoders but never the proxy generator.
    [Test]
    public void FFmpegProxyExtension_IsExported()
    {
        Assert.That(
            typeof(FFmpegProxyExtension).GetCustomAttribute<ExportAttribute>(),
            Is.Not.Null,
            "FFmpegProxyExtension must be [Export]-marked like the decoding/encoding extensions.");
    }

    [Test]
    public void MarkMissing_ArmsReprobeCooldownBeforeNotifyingListeners()
    {
        FFmpegInstallNotifier.MarkInstalled();
        bool cooldownArmedWhenNotified = false;
        EventHandler handler = (_, _) =>
            cooldownArmedWhenNotified = FFmpegLibraryState.ShouldSkipStartProbe(Environment.TickCount64);
        FFmpegLibraryState.AvailabilityChanged += handler;
        try
        {
            FFmpegLibraryState.MarkMissing();

            // A synchronous listener must already observe ShouldSkipStartProbe == true, otherwise it
            // could re-probe the worker before the cooldown is in effect.
            Assert.That(cooldownArmedWhenNotified, Is.True);
        }
        finally
        {
            FFmpegLibraryState.AvailabilityChanged -= handler;
            FFmpegInstallNotifier.MarkInstalled();
        }
    }

    [Test]
    public void Open_WhenLibrariesMissing_ReturnsNullSoRegistryCanFallBack()
    {
        // Force the "libraries missing" short-circuit so the worker start fails without launching a
        // real process. Open must NOT rethrow the typed exception (that would abort DecoderRegistry
        // before it can try a fallback decoder like MediaFoundation for a regular open).
        FFmpegLibraryState.MarkMissing();
        try
        {
            var decoder = new FFmpegDecoderInfo(new FFmpegDecodingSettings());
            string file = Path.Combine(TestContext.CurrentContext.WorkDirectory, "missing.mp4");

            MediaReader? reader = null;
            Assert.That(() => reader = decoder.Open(file, new MediaOptions(MediaMode.Video)), Throws.Nothing);
            reader?.Dispose();
            Assert.That(reader, Is.Null);
        }
        finally
        {
            FFmpegInstallNotifier.MarkInstalled();
        }
    }

    [Test]
    public async Task ProxyGenerator_WhenFFmpegMissing_ThrowsUnavailableSoQueuePauses()
    {
        FFmpegLibraryState.MarkMissing();
        try
        {
            string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var store = new ProxyStore(Path.Combine(root, "proxies"));
            var generator = new FFmpegProxyGenerator(store);
            string source = Path.Combine(root, "clip.mp4");
            File.WriteAllBytes(source, new byte[64]);
            // Fingerprint from the file itself so it matches the generator's pre-encode re-stat;
            // a synthetic mtime would trip the source-changed skip before the missing-FFmpeg path.
            var job = new ProxyJob(ProxyFingerprint.FromFile(source), ProxyPreset.Quarter);

            // The proxy generator translates a missing FFmpeg install into the unavailable signal
            // that ProxyJobQueue uses to pause the batch (instead of draining it as failures).
            await Assert.ThrowsAsync<ProxyGeneratorUnavailableException>(() => generator.GenerateAsync(job).AsTask());
        }
        finally
        {
            FFmpegInstallNotifier.MarkInstalled();
        }
    }

    [Test]
    public void SelectWorker_SendsBackgroundReadsToTheBackgroundWorker()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FFmpegDecoderInfo.SelectWorker(new MediaOptions(MediaMode.Video) { IsBackground = true }),
                Is.SameAs(FFmpegWorkerProcess.BackgroundDecodingInstance));
            Assert.That(FFmpegDecoderInfo.SelectWorker(new MediaOptions(MediaMode.Video)),
                Is.SameAs(FFmpegWorkerProcess.DecodingInstance));
            Assert.That(FFmpegWorkerProcess.BackgroundDecodingInstance, Is.Not.SameAs(FFmpegWorkerProcess.DecodingInstance));
        });
    }

    [Test]
    public void ProxyGenerator_ReadsTheSourceAsBackgroundWork()
    {
        var decoder = new RecordingDecoderInfo();
        DecoderRegistry.Register(decoder);
        try
        {
            string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var generator = new FFmpegProxyGenerator(new ProxyStore(Path.Combine(root, "proxies")));
            string source = Path.Combine(root, "clip" + RecordingDecoderInfo.Extension);
            File.WriteAllBytes(source, new byte[64]);
            var job = new ProxyJob(ProxyFingerprint.FromFile(source), ProxyPreset.Quarter);

            // The decoder opens nothing, so generation fails after the open and the audio probe that follows it.
            Assert.CatchAsync(() => generator.GenerateAsync(job).AsTask());
            Assert.Multiple(() =>
            {
                Assert.That(
                    decoder.Requests.Select(options => options.StreamsToLoad),
                    Is.EqualTo(new[] { MediaMode.Video, MediaMode.Audio }));
                Assert.That(decoder.Requests.Select(options => options.IsBackground), Is.All.True);
            });
        }
        finally
        {
            DecoderRegistry.Unregister(decoder);
        }
    }

    private sealed class RecordingDecoderInfo : IDecoderInfo
    {
        public const string Extension = ".proxysource";

        public List<MediaOptions> Requests { get; } = [];

        public string Name => "Recording Decoder";

        public MediaReader? Open(string file, MediaOptions options)
        {
            Requests.Add(options);
            return null;
        }

        public IEnumerable<string> VideoExtensions() => [Extension];

        public IEnumerable<string> AudioExtensions() => [];
    }
}
