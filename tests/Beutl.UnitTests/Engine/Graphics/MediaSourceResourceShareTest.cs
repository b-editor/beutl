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
        using var encode = videoSource.ToResource(ctxEncode);

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
        using var encode = videoSource.ToResource(
            new CompositionContext(TimeSpan.Zero) { DisableResourceShare = true });
        using var preview = videoSource.ToResource(CompositionContext.Default);

        Assert.That(preview.MediaReader, Is.Not.SameAs(encode.MediaReader),
            "エンコード専用 MediaReader がプレビュー側に漏れてはならない");
    }

    [Test]
    public void SoundSource_SharedByDefault_ReusesMediaReader()
    {
        var videoPath = TestMediaHelper.CreateTestVideoFile(80, 80, new Rational(30, 1), 60);
        var soundSource = new SoundSource();
        soundSource.ReadFrom(new Uri(videoPath));

        using var a = soundSource.ToResource(CompositionContext.Default);
        using var b = soundSource.ToResource(CompositionContext.Default);

        Assert.That(a.MediaReader, Is.Not.Null);
        Assert.That(b.MediaReader, Is.Not.Null);
        Assert.That(b.MediaReader, Is.SameAs(a.MediaReader),
            "DisableResourceShare=false では同じ MediaReader を共有するはず");
    }

    [Test]
    public void SoundSource_DisableResourceShare_YieldsIndependentMediaReader()
    {
        var videoPath = TestMediaHelper.CreateTestVideoFile(80, 80, new Rational(30, 1), 60);
        var soundSource = new SoundSource();
        soundSource.ReadFrom(new Uri(videoPath));

        using var preview = soundSource.ToResource(CompositionContext.Default);
        using var encode = soundSource.ToResource(
            new CompositionContext(TimeSpan.Zero) { DisableResourceShare = true });

        Assert.That(preview.MediaReader, Is.Not.Null);
        Assert.That(encode.MediaReader, Is.Not.Null);
        Assert.That(encode.MediaReader, Is.Not.SameAs(preview.MediaReader),
            "DisableResourceShare=true では専用の MediaReader が割り当てられるはず");
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

    [Test]
    public void ImageSource_DisableResourceShare_DoesNotContaminateSharedCache()
    {
        var uri = TestMediaHelper.CreateTestImageUri(16, 16, Colors.Red);
        var imageSource = new ImageSource();
        imageSource.ReadFrom(uri);

        // エンコード側が先に Resource を生成しても、プレビュー側 (共有モード) は
        // エンコード専用 Bitmap を掴まない
        using var encode = imageSource.ToResource(
            new CompositionContext(TimeSpan.Zero) { DisableResourceShare = true });
        using var preview = imageSource.ToResource(CompositionContext.Default);

        Assert.That(preview.Bitmap, Is.Not.SameAs(encode.Bitmap),
            "エンコード専用 Bitmap がプレビュー側に漏れてはならない");
    }

    [Test]
    public void SoundSource_DisableResourceShare_DoesNotContaminateSharedCache()
    {
        var videoPath = TestMediaHelper.CreateTestVideoFile(80, 80, new Rational(30, 1), 60);
        var soundSource = new SoundSource();
        soundSource.ReadFrom(new Uri(videoPath));

        // エンコード側が先に Resource を生成しても、プレビュー側 (共有モード) は
        // エンコード専用 MediaReader を掴まない
        using var encode = soundSource.ToResource(
            new CompositionContext(TimeSpan.Zero) { DisableResourceShare = true });
        using var preview = soundSource.ToResource(CompositionContext.Default);

        Assert.That(preview.MediaReader, Is.Not.SameAs(encode.MediaReader),
            "エンコード専用 MediaReader がプレビュー側に漏れてはならない");
    }

    [Test]
    public void ImageSource_ReadFromDifferentUri_DoesNotShareStaleCounter()
    {
        // 別 Resource が古い URI の Counter を握ったまま ReadFrom(newUri) → ToResource すると、
        // _bitmapRef を破棄しないと TryAddRef が成功し新 URI でも古い Bitmap が返ってしまう。
        var uriOld = TestMediaHelper.CreateTestImageUri(16, 16, Colors.Red);
        var uriNew = TestMediaHelper.CreateTestImageUri(32, 32, Colors.Blue);

        var imageSource = new ImageSource();
        imageSource.ReadFrom(uriOld);

        using var oldResource = imageSource.ToResource(CompositionContext.Default);
        Assert.That(oldResource.Bitmap, Is.Not.Null);

        imageSource.ReadFrom(uriNew);
        using var newResource = imageSource.ToResource(CompositionContext.Default);

        Assert.That(newResource.Bitmap, Is.Not.Null);
        Assert.That(newResource.Bitmap, Is.Not.SameAs(oldResource.Bitmap),
            "URI 切替後の Resource が旧 URI の Bitmap を共有してはならない");
        Assert.That(newResource.FrameSize, Is.EqualTo(new PixelSize(32, 32)),
            "新 URI に対応する Bitmap がロードされるはず");
    }

    [Test]
    public void VideoSource_ReadFromDifferentUri_DoesNotShareStaleCounter()
    {
        var pathOld = TestMediaHelper.CreateTestVideoFile(80, 80, new Rational(30, 1), 60);
        var pathNew = TestMediaHelper.CreateTestVideoFile(120, 120, new Rational(30, 1), 60);

        var videoSource = new VideoSource();
        videoSource.ReadFrom(new Uri(pathOld));

        using var oldResource = videoSource.ToResource(CompositionContext.Default);
        Assert.That(oldResource.MediaReader, Is.Not.Null);

        videoSource.ReadFrom(new Uri(pathNew));
        using var newResource = videoSource.ToResource(CompositionContext.Default);

        Assert.That(newResource.MediaReader, Is.Not.Null);
        Assert.That(newResource.MediaReader, Is.Not.SameAs(oldResource.MediaReader),
            "URI 切替後の Resource が旧 URI の MediaReader を共有してはならない");
        Assert.That(newResource.MediaReader!.VideoInfo.FrameSize, Is.EqualTo(new PixelSize(120, 120)),
            "新 URI に対応する MediaReader がロードされるはず");
    }

    [Test]
    public void SoundSource_ReadFromDifferentUri_DoesNotShareStaleCounter()
    {
        var pathOld = TestMediaHelper.CreateTestVideoFile(80, 80, new Rational(30, 1), 60);
        var pathNew = TestMediaHelper.CreateTestVideoFile(120, 120, new Rational(30, 1), 60);

        var soundSource = new SoundSource();
        soundSource.ReadFrom(new Uri(pathOld));

        using var oldResource = soundSource.ToResource(CompositionContext.Default);
        Assert.That(oldResource.MediaReader, Is.Not.Null);

        soundSource.ReadFrom(new Uri(pathNew));
        using var newResource = soundSource.ToResource(CompositionContext.Default);

        Assert.That(newResource.MediaReader, Is.Not.Null);
        Assert.That(newResource.MediaReader, Is.Not.SameAs(oldResource.MediaReader),
            "URI 切替後の Resource が旧 URI の MediaReader を共有してはならない");
    }
}
