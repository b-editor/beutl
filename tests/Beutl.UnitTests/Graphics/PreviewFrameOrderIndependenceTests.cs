using System.Security.Cryptography;
using Beutl.Configuration;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Graphics;

/// <summary>
/// The picture a renderer produces for a frame must depend only on that frame. The preview takes two
/// different routes depending on the frame cache: a miss rasterizes through <c>Render</c>, a hit only
/// runs <c>UpdateFrame</c>, which advances the render-node bookkeeping without drawing. Scrubbing
/// interleaves the two, so any state one route leaves behind for the other shows up as a frame drawn
/// with another frame's picture — and with the frame cache on, that picture is then stored under the
/// wrong frame number and shown from then on.
/// </summary>
[TestFixture]
[NonParallelizable]
public class PreviewFrameOrderIndependenceTests
{
    private const int Rate = 30;
    private const int FrameCount = 16;

    // A frame already visited stands for a frame-cache hit and takes the UpdateFrame route; a frame
    // reached for the first time is a miss and has to match the baseline picture.
    private static readonly int[] s_scrub =
    [
        0, 1, 2, 3, 4, 3, 2, 1, 0, 1, 2, 3, 4, 5, 6, 5, 4, 3, 2, 3, 4, 5, 6, 7, 8, 9,
        8, 7, 6, 5, 6, 7, 8, 9, 10, 11, 10, 9, 8, 9, 10, 11, 12, 13, 14, 15,
    ];

    private static readonly int[] s_recheck = [7, 3, 12, 1, 9, 15, 0, 5, 11, 2, 14, 6, 8, 4, 13, 10];

    private IProxyResolver? _oldResolver;
    private PreviewSourceMode _oldPreviewSourceMode;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        TestMediaHelper.RegisterTestDecoder();
    }

    [SetUp]
    public void SetUp()
    {
        _oldResolver = DecoderRegistry.ProxyResolver;
        _oldPreviewSourceMode = GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode;
        DecoderRegistry.ProxyResolver = null;
        GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode = PreviewSourceMode.ForceOriginal;
    }

    [TearDown]
    public void TearDown()
    {
        DecoderRegistry.ProxyResolver = _oldResolver;
        GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode = _oldPreviewSourceMode;
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void RenderedFrameIsTheSameWhetherOrNotCacheHitsPrecededIt(bool preferProxy, bool withEffect)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var media = new SceneMedia(preferProxy, withEffect);

            string[] baseline = new string[FrameCount];
            using (var renderer = new SceneRenderer(media.NewScene()) { CacheOptions = RenderCacheOptions.Default })
            {
                for (int frame = 0; frame < FrameCount; frame++)
                {
                    baseline[frame] = RenderAndHash(renderer, frame);

                    // Re-rendering the same time on the same renderer must reproduce the picture
                    // exactly; anything that does not is non-determinism, not a cache mistake.
                    Assert.That(RenderAndHash(renderer, frame), Is.EqualTo(baseline[frame]),
                        $"frame {frame} rendered differently when rendered twice in a row");
                }
            }

            Assert.That(baseline.Distinct().Count(), Is.EqualTo(FrameCount),
                "the scene has to look different on every frame or the comparisons below prove nothing");

            using (var renderer = new SceneRenderer(media.NewScene()) { CacheOptions = RenderCacheOptions.Default })
            {
                var rendered = new HashSet<int>();
                foreach (int frame in s_scrub)
                {
                    if (!rendered.Add(frame))
                    {
                        renderer.UpdateFrame(renderer.Compositor.EvaluateGraphics(frame.ToTimeSpan(Rate)));
                        continue;
                    }

                    Assert.That(RenderAndHash(renderer, frame), Is.EqualTo(baseline[frame]),
                        $"frame {frame} rendered a different picture after the scrub reached it");
                }

                // Turning the frame cache off and on re-renders frames that were only visited through
                // UpdateFrame while it was on.
                foreach (int frame in s_recheck)
                {
                    Assert.That(RenderAndHash(renderer, frame), Is.EqualTo(baseline[frame]),
                        $"frame {frame} rendered a different picture on the second pass");
                }
            }
        });
    }

    /// <summary>
    /// <see cref="SourceBackdrop"/> composites whatever has already been drawn beneath it, so its
    /// picture comes from render state rather than from the frame's own time. Two of them stacked
    /// over a clip is the shape a real project uses.
    /// </summary>
    [Test]
    public void BackdropFrameIsTheSameWhetherOrNotCacheHitsPrecededIt()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var media = new SceneMedia(preferProxy: true, withEffect: false, withBackdrops: true);

            string[] baseline = new string[FrameCount];
            using (var renderer = new SceneRenderer(media.NewScene()) { CacheOptions = RenderCacheOptions.Default })
            {
                for (int frame = 0; frame < FrameCount; frame++)
                {
                    baseline[frame] = RenderAndHash(renderer, frame);
                    Assert.That(RenderAndHash(renderer, frame), Is.EqualTo(baseline[frame]),
                        $"frame {frame} rendered differently when rendered twice in a row");
                }
            }

            Assert.That(baseline.Distinct().Count(), Is.EqualTo(FrameCount),
                "the scene has to look different on every frame or the comparisons below prove nothing");

            using (var renderer = new SceneRenderer(media.NewScene()) { CacheOptions = RenderCacheOptions.Default })
            {
                var rendered = new HashSet<int>();
                foreach (int frame in s_scrub)
                {
                    if (!rendered.Add(frame))
                    {
                        renderer.UpdateFrame(renderer.Compositor.EvaluateGraphics(frame.ToTimeSpan(Rate)));
                        continue;
                    }

                    Assert.That(RenderAndHash(renderer, frame), Is.EqualTo(baseline[frame]),
                        $"frame {frame} rendered a different picture after the scrub reached it");
                }

                foreach (int frame in s_recheck)
                {
                    Assert.That(RenderAndHash(renderer, frame), Is.EqualTo(baseline[frame]),
                        $"frame {frame} rendered a different picture on the second pass");
                }
            }
        });
    }

    private static string RenderAndHash(SceneRenderer renderer, int frame)
    {
        renderer.Render(renderer.Compositor.EvaluateGraphics(frame.ToTimeSpan(Rate)));
        using Bitmap snapshot = renderer.Snapshot();
        using Bitmap srgb = snapshot.Convert(
            BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
        return Convert.ToHexString(SHA256.HashData(srgb.GetPixelSpan()))[..16];
    }

    private sealed class SceneMedia : IDisposable
    {
        private readonly string _root;
        private readonly bool _preferProxy;
        private readonly bool _withEffect;
        private readonly bool _withBackdrops;
        private readonly ProxyStore? _store;

        public SceneMedia(bool preferProxy, bool withEffect, bool withBackdrops = false)
        {
            _preferProxy = preferProxy;
            _withEffect = withEffect;
            _withBackdrops = withBackdrops;
            _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"order-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_root);

            if (preferProxy)
            {
                _store = new ProxyStore(_root);
                DecoderRegistry.ProxyResolver = new ProxyResolver(_store);
                GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode = PreviewSourceMode.PreferProxy;
            }
        }

        // A single moving clip never lets the render-node cache engage. A real project mixes a static
        // subtree, which does get cached, with clips that enter and leave the playhead's range.
        public Scene NewScene()
        {
            var scene = new Scene(160, 120, string.Empty)
            {
                Uri = new Uri(Path.Combine(_root, $"{Guid.NewGuid():N}.scene")),
            };

            var backdrop = new RectShape();
            backdrop.Width.CurrentValue = 160;
            backdrop.Height.CurrentValue = 120;
            backdrop.Fill.CurrentValue = new SolidColorBrush(Colors.DarkSlateBlue);
            scene.Children.Add(NewElement(backdrop, TimeSpan.Zero, TimeSpan.FromSeconds(4), zIndex: 0));

            scene.Children.Add(NewElement(
                NewClip(new PixelSize(160, 120)), TimeSpan.Zero, TimeSpan.FromSeconds(4), zIndex: 1));

            if (_withBackdrops)
            {
                for (int i = 0; i < 2; i++)
                {
                    var glass = new SourceBackdrop();
                    glass.Opacity.CurrentValue = 70f;
                    glass.Clear.CurrentValue = i == 0;
                    if (i == 1)
                    {
                        glass.FilterEffect.CurrentValue = new MosaicEffect();
                    }

                    scene.Children.Add(NewElement(
                        glass, TimeSpan.Zero, TimeSpan.FromSeconds(4), zIndex: 2 + i));
                }

                return scene;
            }

            var overlay = NewClip(new PixelSize(80, 60));
            overlay.Opacity.CurrentValue = 60f;
            scene.Children.Add(NewElement(
                overlay, TimeSpan.FromSeconds(5d / Rate), TimeSpan.FromSeconds(6d / Rate), zIndex: 2));

            return scene;
        }

        private SourceVideo NewClip(PixelSize size)
        {
            string originalPath = TestMediaHelper.CreateTestVideoFile(
                size.Width, size.Height, new Rational(Rate, 1), FrameCount * 4);
            if (_preferProxy)
            {
                RegisterQuarterProxy(originalPath, size);
            }

            var media = new VideoSource();
            media.ReadFrom(new Uri(originalPath));
            var drawable = new SourceVideo();
            drawable.Source.CurrentValue = media;
            if (_withEffect)
            {
                drawable.FilterEffect.CurrentValue = new MosaicEffect();
            }

            return drawable;
        }

        private void RegisterQuarterProxy(string originalPath, PixelSize originalSize)
        {
            // ProxyFingerprint.FromFile needs a non-empty original; the test decoder reads the
            // dimensions out of the file name, so the bytes themselves are never decoded.
            if (new FileInfo(originalPath).Length == 0)
            {
                File.WriteAllBytes(originalPath, [1, 2, 3, 4]);
            }

            var proxySize = new PixelSize(originalSize.Width / 4, originalSize.Height / 4);
            string template = TestMediaHelper.CreateTestVideoFile(
                proxySize.Width, proxySize.Height, new Rational(Rate, 1), FrameCount * 4);
            File.WriteAllBytes(template, [1, 2, 3, 4]);

            string relative = $"proxy/{Path.GetFileName(template)}";
            string proxyPath = Path.Combine(_root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(proxyPath)!);
            File.Copy(template, proxyPath, overwrite: true);

            var now = DateTime.UtcNow;
            _store!.Register(new ProxyEntry(
                ProxyFingerprint.FromFile(originalPath),
                ProxyPreset.Quarter,
                ProxyState.Ready,
                relative,
                new FileInfo(proxyPath).Length,
                originalSize,
                proxySize,
                now,
                now,
                null));
        }

        private Element NewElement(Drawable drawable, TimeSpan start, TimeSpan length, int zIndex)
        {
            var element = new Element
            {
                Start = start,
                Length = length,
                ZIndex = zIndex,
                IsEnabled = true,
                Uri = new Uri(Path.Combine(_root, $"{Guid.NewGuid():N}.layer")),
            };
            element.AddObject(drawable);
            return element;
        }

        public void Dispose()
        {
            DecoderRegistry.ProxyResolver = null;
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }
    }
}
