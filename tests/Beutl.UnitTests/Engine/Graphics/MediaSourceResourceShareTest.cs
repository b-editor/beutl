using Beutl.Composition;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics;

[TestFixture]
public class MediaSourceResourceShareTest
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        TestMediaHelper.RegisterTestDecoder();
    }

    [Test]
    public void VideoSource_SharedByDefault_ReusesMediaReader()
    {
        var videoPath = TestMediaHelper.CreateTestVideoFile(80, 80, new Rational(30, 1), 60);
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(videoPath));

        using var a = videoSource.ToResource(CompositionContext.Default);
        using var b = videoSource.ToResource(CompositionContext.Default);

        Assert.That(a.MediaReader, Is.Not.Null);
        Assert.That(b.MediaReader, Is.Not.Null);
        Assert.That(b.MediaReader, Is.SameAs(a.MediaReader),
            "DisableResourceShare=false では同じ MediaReader を共有するはず");
    }

    [Test]
    public void VideoSource_DisableResourceShare_YieldsIndependentMediaReader()
    {
        var videoPath = TestMediaHelper.CreateTestVideoFile(80, 80, new Rational(30, 1), 60);
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(videoPath));

        var ctxPreview = CompositionContext.Default;
        var ctxEncode = new CompositionContext(TimeSpan.Zero) { DisableResourceShare = true };

        using var preview = videoSource.ToResource(ctxPreview);
        using var encode = (VideoSource.Resource)videoSource.ToResource(ctxEncode);

        Assert.That(preview.MediaReader, Is.Not.Null);
        Assert.That(encode.MediaReader, Is.Not.Null);
        Assert.That(encode.MediaReader, Is.Not.SameAs(preview.MediaReader),
            "DisableResourceShare=true では専用の MediaReader が割り当てられるはず");
    }

    [Test]
    public void VideoSource_DisableResourceShare_DoesNotContaminateSharedCache()
    {
        var videoPath = TestMediaHelper.CreateTestVideoFile(80, 80, new Rational(30, 1), 60);
        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(videoPath));

        // エンコード側が先に Resource を生成しても、プレビュー側 (共有モード) は
        // エンコード専用 MediaReader を掴まない
        using var encode = (VideoSource.Resource)videoSource.ToResource(
            new CompositionContext(TimeSpan.Zero) { DisableResourceShare = true });
        using var preview = videoSource.ToResource(CompositionContext.Default);

        Assert.That(preview.MediaReader, Is.Not.SameAs(encode.MediaReader),
            "エンコード専用 MediaReader がプレビュー側に漏れてはならない");
    }

    [Test]
    public void ImageSource_SharedByDefault_ReusesBitmap()
    {
        var uri = TestMediaHelper.CreateTestImageUri(16, 16, Colors.Red);
        var imageSource = new ImageSource();
        imageSource.ReadFrom(uri);

        using var a = imageSource.ToResource(CompositionContext.Default);
        using var b = imageSource.ToResource(CompositionContext.Default);

        Assert.That(a.Bitmap, Is.Not.Null);
        Assert.That(b.Bitmap, Is.Not.Null);
        Assert.That(b.Bitmap, Is.SameAs(a.Bitmap),
            "DisableResourceShare=false では同じ Bitmap を共有するはず");
    }

    [Test]
    public void ImageSource_DisableResourceShare_YieldsIndependentBitmap()
    {
        var uri = TestMediaHelper.CreateTestImageUri(16, 16, Colors.Red);
        var imageSource = new ImageSource();
        imageSource.ReadFrom(uri);

        using var preview = imageSource.ToResource(CompositionContext.Default);
        using var encode = imageSource.ToResource(
            new CompositionContext(TimeSpan.Zero) { DisableResourceShare = true });

        Assert.That(preview.Bitmap, Is.Not.Null);
        Assert.That(encode.Bitmap, Is.Not.Null);
        Assert.That(encode.Bitmap, Is.Not.SameAs(preview.Bitmap),
            "DisableResourceShare=true では専用の Bitmap が割り当てられるはず");
    }
}
