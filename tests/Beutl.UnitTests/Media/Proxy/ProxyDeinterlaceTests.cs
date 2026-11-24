using Beutl.Extensions.FFmpeg.Proxy;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Media.Proxy;

[TestFixture]
public sealed class ProxyDeinterlaceTests
{
    [Test]
    public async Task OfflineResolutionAndSidecarRecoveryKeepVariantsSeparate()
    {
        var root = ProxyTestScope.CreateRoot();
        try
        {
            var source = Path.Combine(root, "clip.mp4");
            File.WriteAllBytes(source, [1, 2, 3]);
            var uri = new Uri(source);
            var fingerprint = ProxyFingerprint.FromFile(source);
            var store = new ProxyStore(Path.Combine(root, "proxies"));
            var inactive = new VideoDeinterlaceOptions(DeinterlaceMode.Off, DeinterlaceFieldOrder.TopFieldFirst,
                DeinterlaceMethod.Bwdif, DeinterlaceFrameRateMode.Field);
            var active = inactive with { Mode = DeinterlaceMode.On };
            ProxyEntry off = Register(inactive, DeinterlaceApplicationStatus.Off);
            ProxyEntry on = Register(active, DeinterlaceApplicationStatus.Applied);

            Assert.That(off.Deinterlace, Is.EqualTo(VideoDeinterlaceOptions.Default));
            Assert.That(off.ProxyFileRelative, Is.EqualTo(ProxyPathUtilities.BuildRelativePath(fingerprint, ProxyPreset.Half)));
            Assert.That(on.ProxyFileRelative, Is.Not.EqualTo(off.ProxyFileRelative));
            Assert.That(store.TryGet(fingerprint, ProxyPreset.Half, inactive), Is.EqualTo(off));
            Assert.That(new ProxyStore(store.StoreRootPath).TryGet(fingerprint, ProxyPreset.Half, active), Is.EqualTo(on));

            File.Delete(source);
            File.Delete(Path.Combine(store.StoreRootPath, "index.json"));
            var recovered = new ProxyStore(store.StoreRootPath);
            await recovered.ReconcileAsync(CancellationToken.None);
            var resolver = new ProxyResolver(recovered);
            var solution = resolver.Resolve(uri, ProxyPreset.Half, active);
            Assert.That(solution?.DeinterlaceStatus, Is.EqualTo(DeinterlaceApplicationStatus.Applied));
            Assert.That(solution?.DetectedFieldOrder, Is.EqualTo(VideoFieldOrder.TopFieldFirst));
            Assert.That(resolver.Resolve(uri, ProxyPreset.Half, inactive)?.Deinterlace, Is.EqualTo(default(VideoDeinterlaceOptions)));
            Assert.That(resolver.Resolve(uri, ProxyPreset.Half, active with { Method = DeinterlaceMethod.Yadif }), Is.Null);

            string onPath = solution!.AbsoluteProxyFilePath;
            using (resolver.Pin(solution))
            {
                Assert.That(recovered.Delete(fingerprint, ProxyPreset.Half, active), Is.True);
                Assert.That(File.Exists(onPath), Is.True);
                Assert.That(resolver.Resolve(uri, ProxyPreset.Half, active), Is.Null);
                Assert.That(resolver.Resolve(uri, ProxyPreset.Half, inactive), Is.Not.Null);
            }
            Assert.That(() => File.Exists(onPath), Is.False.After(2000, 10));
            await recovered.FlushAsync(CancellationToken.None);

            ProxyEntry Register(VideoDeinterlaceOptions options, DeinterlaceApplicationStatus status)
            {
                string relative = ProxyPathUtilities.BuildRelativePath(fingerprint, ProxyPreset.Half, deinterlace: options);
                string path = Path.Combine(store.StoreRootPath, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, [1, 2, 3]);
                DateTime now = DateTime.UtcNow;
                var entry = new ProxyEntry(fingerprint, ProxyPreset.Half, ProxyState.Ready, relative, 3,
                    new PixelSize(64, 32), new PixelSize(32, 16), now, now, null)
                {
                    Deinterlace = options,
                    DeinterlaceStatus = status,
                    DetectedFieldOrder = VideoFieldOrder.TopFieldFirst,
                };
                store.Register(entry);
                FFmpegProxyGenerator.WriteMetadata(path, entry);
                return entry;
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
