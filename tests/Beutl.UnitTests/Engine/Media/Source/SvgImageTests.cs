using System.Net;
using System.Net.Sockets;
using System.Text;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.Serialization;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Media.Source;

[TestFixture]
[NonParallelizable]
public sealed class SvgImageTests
{
    // The left half is opaque red, and the edge is on a whole logical pixel.
    private const string HalfRed = """
        <svg xmlns="http://www.w3.org/2000/svg" width="20" height="10">
          <rect width="10" height="10" fill="#ff0000"/>
        </svg>
        """;

    private static readonly Bgra8888 s_green = new(10, 200, 30, 255);

    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("beutl-svg-image-").FullName;
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Test]
    public void An_SVG_opens_at_the_size_it_declares()
    {
        using ImageSource.Resource resource = Open(HalfRed);

        Assert.Multiple(() =>
        {
            Assert.That(resource.IsOffline, Is.False);
            Assert.That(resource.FrameSize, Is.EqualTo(new PixelSize(20, 10)));
        });
    }

    [Test]
    public void An_SVG_with_only_a_view_box_opens_at_the_size_of_the_view_box()
    {
        using ImageSource.Resource resource = Open("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="5 5 30 40">
              <rect x="5" y="5" width="30" height="40" fill="#ff0000"/>
            </svg>
            """);

        Assert.Multiple(() =>
        {
            Assert.That(resource.FrameSize, Is.EqualTo(new PixelSize(30, 40)));
            Assert.That(ReadPixel(resource.Bitmap!, 1, 1), Is.EqualTo(new Bgra8888(255, 0, 0, 255)));
        });
    }

    [Test]
    public void An_SVG_has_pixels_for_what_cannot_take_a_drawing()
    {
        using ImageSource.Resource resource = Open(HalfRed);

        Bitmap bitmap = resource.Bitmap!;

        Assert.Multiple(() =>
        {
            Assert.That(new PixelSize(bitmap.Width, bitmap.Height), Is.EqualTo(new PixelSize(20, 10)));
            Assert.That(ReadPixel(bitmap, 5, 5), Is.EqualTo(new Bgra8888(255, 0, 0, 255)));
            Assert.That(ReadPixel(bitmap, 15, 5).A, Is.Zero);
        });
    }

    [Test]
    public void An_SVG_in_a_data_URI_opens()
    {
        var source = new ImageSource();
        source.ReadFrom(UriHelper.CreateBase64DataUri("image/svg+xml", Encoding.UTF8.GetBytes(HalfRed)));

        using ImageSource.Resource resource = source.ToResource(CompositionContext.Default);

        Assert.That(resource.FrameSize, Is.EqualTo(new PixelSize(20, 10)));
    }

    [Test]
    public void A_file_that_is_not_an_SVG_shows_as_offline()
    {
        using ImageSource.Resource resource = Open("not an svg");

        Assert.Multiple(() =>
        {
            Assert.That(resource.IsOffline, Is.True);
            Assert.That(resource.FrameSize, Is.EqualTo(OfflineMediaPlaceholder.Size));
            Assert.That(resource.Bitmap, Is.Not.Null);
        });
    }

    [TestCase("""<image href="{0}/a.png" width="10" height="10"/>""")]
    [TestCase("""<image xmlns:xlink="http://www.w3.org/1999/xlink" xlink:href="{0}/a.png" width="10" height="10"/>""")]
    [TestCase("""<use href="{0}/b.svg#shape"/>""")]
    [TestCase("""<style>@import url("{0}/c.css");</style>""")]
    [TestCase("""<link xmlns="http://www.w3.org/1999/xhtml" rel="stylesheet" href="{0}/d.css"/>""")]
    [TestCase("""<style>@font-face {{ font-family: remote; src: url("{0}/f.ttf"); }}</style><text font-family="remote">a</text>""")]
    public async Task An_SVG_does_not_contact_another_host(string element)
    {
        int connections = await CountConnectionsWhileOpening(url => $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="10" height="10">
              {string.Format(element, url)}
              <rect width="10" height="10" fill="#ff0000"/>
            </svg>
            """);

        Assert.That(connections, Is.Zero);
    }

    [TestCase("""<!DOCTYPE svg SYSTEM "{0}/e.dtd">""")]
    [TestCase("""<!DOCTYPE svg [<!ENTITY remote SYSTEM "{0}/e.txt">]>""")]
    [TestCase("""<?xml-stylesheet type="text/css" href="{0}/g.css"?>""")]
    public async Task An_SVG_does_not_fetch_a_definition_from_another_host(string doctype)
    {
        int connections = await CountConnectionsWhileOpening(url => $"""
            <?xml version="1.0"?>
            {string.Format(doctype, url)}
            <svg xmlns="http://www.w3.org/2000/svg" width="10" height="10">
              <text>{(doctype.Contains("remote") ? "&remote;" : "")}</text>
            </svg>
            """);

        Assert.That(connections, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void An_SVG_does_not_read_another_file(bool absolute)
    {
        string image = WriteGreenPng("other.png");
        using ImageSource.Resource resource = Open($"""
            <svg xmlns="http://www.w3.org/2000/svg" width="10" height="10">
              <image href="{(absolute ? new Uri(image).AbsoluteUri : "other.png")}" width="10" height="10"/>
            </svg>
            """);

        // The library paints a placeholder where an image is blocked or missing.
        Assert.Multiple(() =>
        {
            Assert.That(resource.IsOffline, Is.False);
            Assert.That(ReadPixel(resource.Bitmap!, 5, 5), Is.Not.EqualTo(s_green));
        });
    }

    [Test]
    public void An_SVG_draws_an_image_embedded_in_it()
    {
        string image = WriteGreenPng("embedded.png");
        using ImageSource.Resource resource = Open($"""
            <svg xmlns="http://www.w3.org/2000/svg" width="10" height="10">
              <image href="data:image/png;base64,{Convert.ToBase64String(File.ReadAllBytes(image))}" width="10" height="10"/>
            </svg>
            """);

        Assert.That(ReadPixel(resource.Bitmap!, 5, 5), Is.EqualTo(s_green));
    }

    [Test]
    public void An_SVG_draws_what_it_refers_to_within_itself()
    {
        using ImageSource.Resource resource = Open("""
            <svg xmlns="http://www.w3.org/2000/svg" width="10" height="10">
              <defs><rect id="shape" width="10" height="10" fill="#ff0000"/></defs>
              <use href="#shape"/>
            </svg>
            """);

        Assert.That(ReadPixel(resource.Bitmap!, 5, 5), Is.EqualTo(new Bgra8888(255, 0, 0, 255)));
    }

    [TestCase("100000", "10")]
    [TestCase("10", "100000")]
    [TestCase("1e30", "1e30")]
    public void An_SVG_that_declares_an_enormous_size_shows_as_offline(string width, string height)
    {
        using ImageSource.Resource resource = Open(
            $"""<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}"/>""");

        Assert.That(resource.IsOffline, Is.True);
    }

    [Test]
    public void An_SVG_at_the_largest_size_opens()
    {
        using ImageSource.Resource resource = Open(
            """<svg xmlns="http://www.w3.org/2000/svg" width="16384" height="1"/>""");

        Assert.That(resource.FrameSize, Is.EqualTo(new PixelSize(16384, 1)));
    }

    // Older Illustrator files declare their namespaces this way.
    [Test]
    public void An_SVG_that_declares_entities_opens()
    {
        using ImageSource.Resource resource = Open("""
            <?xml version="1.0"?>
            <!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd" [
              <!ENTITY ns_svg "http://www.w3.org/2000/svg">
            ]>
            <svg xmlns="&ns_svg;" width="20" height="10">
              <rect width="10" height="10" fill="#ff0000"/>
            </svg>
            """);

        Assert.Multiple(() =>
        {
            Assert.That(resource.IsOffline, Is.False);
            Assert.That(ReadPixel(resource.Bitmap!, 5, 5), Is.EqualTo(new Bgra8888(255, 0, 0, 255)));
        });
    }

    [Test]
    public void An_SVG_whose_entities_expand_without_end_does_not_stall()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        using ImageSource.Resource resource = Open("""
            <?xml version="1.0"?>
            <!DOCTYPE svg [
              <!ENTITY a "aaaaaaaaaa">
              <!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;">
              <!ENTITY c "&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;">
              <!ENTITY d "&c;&c;&c;&c;&c;&c;&c;&c;&c;&c;">
              <!ENTITY e "&d;&d;&d;&d;&d;&d;&d;&d;&d;&d;">
              <!ENTITY f "&e;&e;&e;&e;&e;&e;&e;&e;&e;&e;">
              <!ENTITY g "&f;&f;&f;&f;&f;&f;&f;&f;&f;&f;">
              <!ENTITY h "&g;&g;&g;&g;&g;&g;&g;&g;&g;&g;">
              <!ENTITY i "&h;&h;&h;&h;&h;&h;&h;&h;&h;&h;">
            ]>
            <svg xmlns="http://www.w3.org/2000/svg" width="10" height="10">
              <text>&i;</text>
            </svg>
            """);
        TestContext.Out.WriteLine($"offline={resource.IsOffline} elapsed={watch.Elapsed}");

        Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
    }

    private string WriteGreenPng(string name)
    {
        string path = Path.Combine(_directory, name);
        using var bitmap = new Bitmap(10, 10, BitmapColorType.Bgra8888, BitmapAlphaType.Opaque, BitmapColorSpace.Srgb);
        bitmap.GetPixelSpan<Bgra8888>().Fill(s_green);
        Assert.That(bitmap.Save(path, EncodedImageFormat.Png), Is.True);
        return path;
    }

    private async Task<int> CountConnectionsWhileOpening(Func<string, string> content)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var stop = new CancellationTokenSource();
        int connections = 0;
        Task accepting = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    using TcpClient client = await listener.AcceptTcpClientAsync(stop.Token);
                    Interlocked.Increment(ref connections);
                }
            }
            catch (OperationCanceledException)
            {
            }
        });

        using (ImageSource.Resource resource = Open(content($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}")))
        {
            Assert.That(resource.Bitmap, Is.Not.Null);
        }

        stop.Cancel();
        await accepting;
        return connections;
    }

    [Test]
    public void An_SVG_reports_the_vector_density_and_a_decoded_image_does_not()
    {
        using ImageSource.Resource drawing = Open(HalfRed);
        var image = new ImageSource();
        image.ReadFrom(TestMediaHelper.CreateTestImageUri(16, 16, Colors.Red));
        using ImageSource.Resource pixels = image.ToResource(CompositionContext.Default);

        Assert.Multiple(() =>
        {
            Assert.That(Measure(drawing).EffectiveScale.IsUnbounded, Is.True);
            Assert.That(Measure(pixels).EffectiveScale.IsUnbounded, Is.False);
        });
    }

    [Test]
    public void An_SVG_stays_sharp_when_it_is_scaled_up()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var source = new ImageSource();
            source.ReadFrom(new Uri(Write(HalfRed)));
            var image = new SourceImage
            {
                Source = { Value = source },
                AlignmentX = { Value = AlignmentX.Left },
                AlignmentY = { Value = AlignmentY.Top },
            };
            using Drawable.Resource resource = image.ToResource(CompositionContext.Default);

            using Bitmap rendered = GoldenImageHarness.RenderAtScale(resource, new PixelSize(20, 10), 8f);

            // The edge is at device x = 80. Pixels at 8x would blur across a few of these.
            Assert.Multiple(() =>
            {
                Assert.That(ReadRed(rendered, 78, 40), Is.EqualTo(1f).Within(0.001f));
                Assert.That(ReadRed(rendered, 79, 40), Is.EqualTo(1f).Within(0.001f));
                Assert.That(ReadRed(rendered, 80, 40), Is.EqualTo(0f).Within(0.001f));
                Assert.That(ReadRed(rendered, 81, 40), Is.EqualTo(0f).Within(0.001f));
            });
        });
    }

    private ImageSource.Resource Open(string content)
    {
        var source = new ImageSource();
        source.ReadFrom(new Uri(Write(content)));
        return source.ToResource(CompositionContext.Default);
    }

    private string Write(string content)
    {
        string path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".svg");
        File.WriteAllText(path, content);
        return path;
    }

    private static RenderNodeMeasurement Measure(ImageSource.Resource source)
    {
        using var node = new ImageSourceRenderNode(source, Brushes.Resource.White, null);
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
        });
        return renderer.Measure();
    }

    private static Bgra8888 ReadPixel(Bitmap bitmap, int x, int y) => bitmap.GetRow<Bgra8888>(y)[x];

    private static float ReadRed(Bitmap bitmap, int x, int y)
    {
        ReadOnlySpan<ushort> row = bitmap.GetRow<ushort>(y);
        return (float)BitConverter.UInt16BitsToHalf(row[x * 4]);
    }
}
