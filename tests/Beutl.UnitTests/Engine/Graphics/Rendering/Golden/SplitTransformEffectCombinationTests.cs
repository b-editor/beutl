using System.IO;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Guards the SplitEffect + TransformEffect(ApplyToTarget=false) combination (both orders).
// With BEUTL_SNAPSHOT_DIR set, renders are saved as PNGs; with BEUTL_MAIN_SNAPSHOT_DIR set,
// the MatchesMain tests compare the fixed-branch render against the main-branch baseline.
[NonParallelizable]
[TestFixture]
public class SplitTransformEffectCombinationTests
{
    private static readonly PixelSize Frame = new(200, 200);

    private static string SnapshotDir =>
        Environment.GetEnvironmentVariable("BEUTL_SNAPSHOT_DIR") ?? "/tmp/st-fixed";

    private static string MainSnapshotDir =>
        Environment.GetEnvironmentVariable("BEUTL_MAIN_SNAPSHOT_DIR") ?? "/tmp/st-main";

    private static SplitEffect MakeSplit()
    {
        var e = new SplitEffect();
        e.HorizontalDivisions.CurrentValue = 3;
        e.VerticalDivisions.CurrentValue = 3;
        e.HorizontalSpacing.CurrentValue = 12;
        e.VerticalSpacing.CurrentValue = 12;
        return e;
    }

    private static TransformEffect MakeTransformFilter()
    {
        var group = new TransformGroup();
        var rot = new RotationTransform();
        rot.Rotation.CurrentValue = 45f;
        var scale = new ScaleTransform();
        scale.ScaleX.CurrentValue = 120f;
        scale.ScaleY.CurrentValue = 100f;
        group.Children.Add(rot);
        group.Children.Add(scale);
        var e = new TransformEffect();
        e.Transform.CurrentValue = group;
        e.ApplyToTarget.CurrentValue = false;
        return e;
    }

    private static Drawable.Resource MakeWithEffect(FilterEffect effect)
    {
        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        shape.Width.CurrentValue = 140;
        shape.Height.CurrentValue = 90;
        shape.Fill.CurrentValue = Brushes.White;
        var rotation = new RotationTransform();
        rotation.Rotation.CurrentValue = 21f;
        shape.Transform.CurrentValue = rotation;
        shape.FilterEffect.CurrentValue = effect;
        return shape.ToResource(CompositionContext.Default);
    }

    private static void SavePng(Bitmap bmp, string name)
    {
        Directory.CreateDirectory(SnapshotDir);
        string path = Path.Combine(SnapshotDir, name);
        using SKImage img = SKImage.FromBitmap(bmp.SKBitmap);
        using SKData data = img.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.OpenWrite(path);
        data.SaveTo(fs);
        TestContext.WriteLine($"Saved {path}");
    }

    private static Bitmap LoadPng(string dir, string name)
    {
        string path = Path.Combine(dir, name);
        using SKBitmap src = SKBitmap.Decode(path);
        // ImageMetrics requires linear-sRGB premultiplied RgbaF16.
        var info = new SKImageInfo(src.Width, src.Height, SKColorType.RgbaF16, SKAlphaType.Premul,
            SKColorSpace.CreateSrgbLinear());
        var dst = new SKBitmap(info);
        using (var canvas = new SKCanvas(dst))
        {
            canvas.DrawBitmap(src, 0, 0);
        }

        return new Bitmap(dst);
    }

    [Test]
    public void SplitThenTransformFilter_RendersWithoutException()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var group = new FilterEffectGroup();
            group.Children.Add(MakeSplit());
            group.Children.Add(MakeTransformFilter());
            using Bitmap bmp = GoldenImageHarness.RenderAtScale(MakeWithEffect(group), Frame, 1f);
            SavePng(bmp, "split-then-transform.png");
        });
    }

    [Test]
    public void TransformFilterThenSplit_RendersWithoutException()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var group = new FilterEffectGroup();
            group.Children.Add(MakeTransformFilter());
            group.Children.Add(MakeSplit());
            using Bitmap bmp = GoldenImageHarness.RenderAtScale(MakeWithEffect(group), Frame, 1f);
            SavePng(bmp, "transform-then-split.png");
        });
    }

    [Test]
    public void SplitThenTransformFilter_MatchesMain()
    {
        VulkanTestEnvironment.EnsureAvailable();
        AssertMainMatch("split-then-transform.png", "SplitThenTransform");
    }

    [Test]
    public void TransformFilterThenSplit_MatchesMain()
    {
        VulkanTestEnvironment.EnsureAvailable();
        AssertMainMatch("transform-then-split.png", "TransformThenSplit");
    }

    private static void AssertMainMatch(string name, string label)
    {
        string mainPath = Path.Combine(MainSnapshotDir, name);
        if (!File.Exists(mainPath))
        {
            Assert.Ignore($"main baseline not found at {mainPath}");
            return;
        }

        using Bitmap fixedBmp = LoadPng(SnapshotDir, name);
        using Bitmap mainBmp = LoadPng(MainSnapshotDir, name);
        double ssim = ImageMetrics.Ssim(fixedBmp, mainBmp);
        double mae = ImageMetrics.MeanAbsoluteError(fixedBmp, mainBmp);
        TestContext.WriteLine($"{label} SSIM={ssim:F4} MAE={mae:F6}");
        Assert.That(ssim, Is.GreaterThan(0.99), $"{label} diverged from main");
    }
}
