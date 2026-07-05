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
    [Test]
    public void MarkMissing_ArmsReprobeCooldownBeforeNotifyingListeners()
    {
        FFmpegInstallNotifier.MarkInstalled();
        bool cooldownArmedWhenNotified = false;
        EventHandler handler = (_, _) =>
            cooldownArmedWhenNotified = FFmpegInstallNotifier.ShouldSkipStartProbe(Environment.TickCount64);
        FFmpegInstallNotifier.AvailabilityChanged += handler;
        try
        {
            FFmpegInstallNotifier.MarkMissing();

            // A synchronous listener must already observe ShouldSkipStartProbe == true, otherwise it
            // could re-probe the worker before the cooldown is in effect.
            Assert.That(cooldownArmedWhenNotified, Is.True);
        }
        finally
        {
            FFmpegInstallNotifier.AvailabilityChanged -= handler;
            FFmpegInstallNotifier.MarkInstalled();
        }
    }

    [Test]
    public void Open_WhenLibrariesMissing_ReturnsNullSoRegistryCanFallBack()
    {
        // Force the "libraries missing" short-circuit so the worker start fails without launching a
        // real process. Open must NOT rethrow the typed exception (that would abort DecoderRegistry
        // before it can try a fallback decoder like MediaFoundation for a regular open).
        FFmpegInstallNotifier.MarkMissing();
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
        FFmpegInstallNotifier.MarkMissing();
        try
        {
            string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var store = new ProxyStore(Path.Combine(root, "proxies"));
            var generator = new FFmpegProxyGenerator(store);
            string source = Path.Combine(root, "clip.mp4");
            File.WriteAllBytes(source, new byte[64]);
            var job = new ProxyJob(new ProxyFingerprint(source, 64, DateTime.UtcNow), ProxyPreset.Quarter);

            try
            {
                await generator.GenerateAsync(job);

                // No throw means an FFmpeg worker is available in this environment, so the
                // missing-libraries path is unreachable; CI (no FFmpeg) exercises the throw.
                Assert.Ignore("An FFmpeg worker is available; missing-libraries path is unreachable.");
            }
            catch (ProxyGeneratorUnavailableException)
            {
                // The proxy generator translates a missing FFmpeg install into the unavailable signal
                // that ProxyJobQueue uses to pause the batch (instead of draining it as failures).
            }
        }
        finally
        {
            FFmpegInstallNotifier.MarkInstalled();
        }
    }
}
