using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using static Beutl.PublicApiContractTests.ContractTestSupport;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class CustomEffectPlacementContractTests
{
    private static readonly Rect s_contentBounds = new(8.25f, 7.125f, 13.375f, 9.5f);
    private static readonly Size s_frameSize = new(32, 24);

    [Test]
    public void ReplacementPreservesFractionalCoverage([Values] bool hasBoundsTransform, [Values(1, 2)] int scale)
    {
        var effect = new CallbackEffect(context =>
        {
            if (hasBoundsTransform)
                context.CustomEffectKeepingPlacement(0, Copy, static (_, bounds) => bounds);
            else
                context.CustomEffectKeepingPlacement(0, Copy);
        });

        using Bitmap expected = Render(scale);
        using Bitmap actual = Render(scale, effect);
        PixelRect footprint = PixelRect.FromRect(s_contentBounds, scale);
        Assert.Multiple(() =>
        {
            Assert.That(AlphaAt(expected, footprint.Right - 1, footprint.Center.Y), Is.InRange(0.1f, 0.9f));
            Assert.That(AlphaAt(expected, footprint.Center.X, footprint.Bottom - 1), Is.InRange(0.1f, 0.9f));
            Assert.That(MaximumAlphaDifference(expected, actual), Is.LessThanOrEqualTo(1f / 255f));
        });

        if (Environment.GetEnvironmentVariable("BEUTL_HEADLESS_CAPTURE_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            string variant = hasBoundsTransform ? "declared" : "runtime";
            expected.Save(Path.Combine(directory, $"placement-{scale}x-{variant}-original.png"));
            actual.Save(Path.Combine(directory, $"placement-{scale}x-{variant}-replacement.png"));
        }

        static void Copy(int _, CustomFilterEffectContext execution)
        {
            EffectTarget source = execution.Targets.Single();
            Rect content = source.ContentBounds ?? throw new AssertionException("Expected fractional content bounds.");
            Assert.That(content.Size, Is.EqualTo(s_contentBounds.Size));
            Assert.That(source.Placement.DeviceBounds, Is.EqualTo(source.Placement.ToDevice(content)));
            Replace(execution, source, content, source.Bounds.Size);
        }
    }

    [Test]
    public void FollowingTransformUsesReplacementContentBounds()
    {
        Rect? croppedBounds = null;
        Rect? transformInput = null;
        var effect = new CallbackEffect(context =>
        {
            context.CustomEffectKeepingPlacement(0, (_, execution) =>
            {
                EffectTarget source = execution.Targets.Single();
                Rect content = source.ContentBounds ?? throw new AssertionException("Expected fractional content bounds.");
                Rect crop = source.Bounds.WithWidth(source.Bounds.Width - 1 / source.Placement.Scale.Value);
                Rect output = content.Intersect(crop);
                Replace(execution, source, output, crop.Size);
                croppedBounds = output;
            });
            context.Transform(0, (_, bounds) =>
            {
                transformInput = bounds;
                Matrix origin = Matrix.CreateTranslation(bounds.Center.X, bounds.Center.Y);
                return -origin * Matrix.CreateScale(1.25f, 1.25f) * origin;
            }, BitmapInterpolationMode.Default);
        });

        using Bitmap rendered = Render(1, effect);
        Assert.That(croppedBounds, Is.Not.Null);
        Assert.That(transformInput, Is.EqualTo(croppedBounds));
    }

    private static void Replace(CustomFilterEffectContext execution, EffectTarget source, Rect content, Size clipSize)
    {
        EffectTarget replacement = execution.CreateTargetLike(source);
        try
        {
            using (ImmediateCanvas canvas = execution.Open(replacement))
            using (canvas.PushClip(new Rect(clipSize)))
                source.Draw(canvas);

            replacement.ContentBounds = content;
            execution.Targets[0] = replacement;
        }
        catch
        {
            replacement.Dispose();
            throw;
        }
    }

    private static Bitmap Render(int scale, FilterEffect? effect = null)
    {
        using FilterEffect.Resource? resource = effect?.ToResource(CompositionContext.Default);
        FilterEffectRenderNode? effectNode = resource?.CreateRenderNode();
        RenderNode source = new RectangleRenderNode(s_contentBounds, Brushes.Resource.White, null);
        using RenderNode root = effectNode ?? source;
        effectNode?.AddChild(source);

        using var target = new CpuRenderTarget(new PixelSize((int)s_frameSize.Width * scale, (int)s_frameSize.Height * scale));
        using var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, density: scale, logicalSize: s_frameSize);
        using var renderer = new RenderNodeRenderer(root, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Delivery,
            CacheOptions = RenderCacheOptions.Disabled,
        }, new CpuTargetFactory());
        canvas.Clear();
        renderer.Render(canvas);
        return target.Snapshot();
    }

    private static float MaximumAlphaDifference(Bitmap expected, Bitmap actual)
    {
        float maximum = 0;
        for (int y = 0; y < expected.Height; y++)
        {
            ReadOnlySpan<Half> expectedRow = expected.GetRow<Half>(y);
            ReadOnlySpan<Half> actualRow = actual.GetRow<Half>(y);
            for (int x = 0; x < expected.Width; x++)
            {
                int alpha = x * 4 + 3;
                maximum = Math.Max(maximum, Math.Abs((float)expectedRow[alpha] - (float)actualRow[alpha]));
            }
        }
        return maximum;
    }

    [SuppressResourceClassGeneration]
    private sealed partial class CallbackEffect(Action<FilterEffectContext> record) : FilterEffect
    {
        public override Resource ToResource(CompositionContext context)
        {
            var resource = new Resource(record);
            bool versionBumped = false;
            resource.Reconcile(this, context, ref versionBumped);
            return resource;
        }

        public new sealed class Resource(Action<FilterEffectContext> record) : FilterEffect.Resource
        {
            protected override void ApplyTo(FilterEffectContext context) => record(context);
        }
    }
}
