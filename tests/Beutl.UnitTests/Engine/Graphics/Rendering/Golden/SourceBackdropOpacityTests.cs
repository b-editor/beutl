using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

/// <summary>
/// <see cref="SourceBackdrop"/> overrides <see cref="Drawable.Render"/> to capture the scene before it draws,
/// so the inherited <see cref="Drawable.Opacity"/> has to be applied there explicitly. These tests pin that
/// the drawable's own opacity fades the captured image the same way an enclosing group does, and that it
/// leaves the <see cref="SourceBackdrop.Clear"/> step untouched.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class SourceBackdropOpacityTests
{
    private static readonly PixelSize s_frame = new(256, 144);

    // One 8-bit code. The group reference and the bare backdrop reach the same blend through different
    // paint paths, so they may round differently by one storage step.
    private const double Tolerance = 1.0 / 255.0;

    [TestCase(100f, 1f)]
    [TestCase(50f, 1f)]
    [TestCase(0f, 1f)]
    [TestCase(50f, 2f)]
    public void OwnOpacity_MatchesTheSameOpacityOnAnEnclosingGroup(float opacity, float scale)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource expected = CreateScene(new BackdropSetup(100f, Clear: false, GroupOpacity: opacity));
            using Drawable.Resource actual = CreateScene(new BackdropSetup(opacity, Clear: false, GroupOpacity: null));
            using Bitmap expectedBitmap = GoldenImageHarness.RenderAtScale(expected, s_frame, scale);
            using Bitmap actualBitmap = GoldenImageHarness.RenderAtScale(actual, s_frame, scale);

            RgbaMaximumError error = ImageMetrics.MaximumAbsoluteErrorPerChannel(expectedBitmap, actualBitmap);
            Assert.That(
                error.Maximum,
                Is.LessThanOrEqualTo(Tolerance),
                $"A bare SourceBackdrop at opacity {opacity} must match a group at that opacity around an opaque backdrop at scale {scale}: {error}.");
        });
    }

    [Test]
    public void OwnOpacityZero_LeavesTheSceneUntouched()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource scene = CreateScene(setup: null);
            using Drawable.Resource invisible = CreateScene(new BackdropSetup(0f, Clear: false, GroupOpacity: null));
            using Drawable.Resource visible = CreateScene(new BackdropSetup(100f, Clear: false, GroupOpacity: null));
            using Bitmap sceneBitmap = GoldenImageHarness.RenderAtScale(scene, s_frame, 1f);
            using Bitmap invisibleBitmap = GoldenImageHarness.RenderAtScale(invisible, s_frame, 1f);
            using Bitmap visibleBitmap = GoldenImageHarness.RenderAtScale(visible, s_frame, 1f);

            // The control: the inverting backdrop must visibly change the scene, or "untouched" proves nothing.
            Assert.That(
                ImageMetrics.MaximumAbsoluteErrorPerChannel(sceneBitmap, visibleBitmap).Maximum,
                Is.GreaterThan(0.5),
                "The fixture's backdrop must visibly invert the scene.");
            GoldenImageHarness.AssertByteIdentical(sceneBitmap, invisibleBitmap);
        });
    }

    [Test]
    public void OwnOpacity_BlendsAClearedBackdropLinearlyOverTheClearedCanvas()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource none = CreateScene(new BackdropSetup(0f, Clear: true, GroupOpacity: null));
            using Drawable.Resource half = CreateScene(new BackdropSetup(50f, Clear: true, GroupOpacity: null));
            using Drawable.Resource full = CreateScene(new BackdropSetup(100f, Clear: true, GroupOpacity: null));
            using Bitmap noneBitmap = GoldenImageHarness.RenderAtScale(none, s_frame, 1f);
            using Bitmap halfBitmap = GoldenImageHarness.RenderAtScale(half, s_frame, 1f);
            using Bitmap fullBitmap = GoldenImageHarness.RenderAtScale(full, s_frame, 1f);

            // Clear runs before the opacity is applied, so opacity 0 shows the cleared canvas and opacity 100
            // the full effect; 50 must sit exactly between them in premultiplied terms.
            Assert.That(
                ImageMetrics.MaximumAbsoluteErrorPerChannel(noneBitmap, fullBitmap).Maximum,
                Is.GreaterThan(0.5),
                "The fixture's cleared backdrop must differ from the cleared canvas.");
            Assert.That(
                MaximumLerpError(noneBitmap, fullBitmap, halfBitmap, 0.5f),
                Is.LessThanOrEqualTo(Tolerance),
                "Opacity 50 on a clearing backdrop must be the midpoint of opacity 0 and opacity 100.");
        });
    }

    private static double MaximumLerpError(Bitmap from, Bitmap to, Bitmap actual, float t)
    {
        ReadOnlySpan<ushort> fromPixels = from.GetPixelSpan<ushort>();
        ReadOnlySpan<ushort> toPixels = to.GetPixelSpan<ushort>();
        ReadOnlySpan<ushort> actualPixels = actual.GetPixelSpan<ushort>();
        Assert.That(actualPixels.Length, Is.EqualTo(fromPixels.Length).And.EqualTo(toPixels.Length));

        double maximum = 0;
        for (int i = 0; i < actualPixels.Length; i++)
        {
            float expected = float.Lerp(
                (float)BitConverter.UInt16BitsToHalf(fromPixels[i]),
                (float)BitConverter.UInt16BitsToHalf(toPixels[i]),
                t);
            float observed = (float)BitConverter.UInt16BitsToHalf(actualPixels[i]);
            maximum = Math.Max(maximum, Math.Abs(observed - expected));
        }

        return maximum;
    }

    private readonly record struct BackdropSetup(float Opacity, bool Clear, float? GroupOpacity);

    private static Drawable.Resource CreateScene(BackdropSetup? setup)
    {
        var scene = new DrawableGroup();
        scene.Children.Add(CreateRectangle(s_frame.Width, s_frame.Height, Colors.DimGray));
        scene.Children.Add(CreateRectangle(130, 95, Colors.Navy));

        if (setup is { } backdropSetup)
        {
            var backdrop = new SourceBackdrop();
            backdrop.FilterEffect.CurrentValue = new Invert();
            backdrop.Clear.CurrentValue = backdropSetup.Clear;
            backdrop.Opacity.CurrentValue = backdropSetup.Opacity;
            if (backdropSetup.GroupOpacity is { } groupOpacity)
            {
                var group = new DrawableGroup();
                group.Opacity.CurrentValue = groupOpacity;
                group.Children.Add(backdrop);
                scene.Children.Add(group);
            }
            else
            {
                scene.Children.Add(backdrop);
            }
        }

        return scene.ToResource(CompositionContext.Default);
    }

    private static RectShape CreateRectangle(float width, float height, Color color)
    {
        var shape = new RectShape();
        shape.Width.CurrentValue = width;
        shape.Height.CurrentValue = height;
        shape.Fill.CurrentValue = new SolidColorBrush(color);
        return shape;
    }
}
