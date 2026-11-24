using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// A layer spans all of its content, which can reach far past the frame: particles travelling off it, a shockwave
// scaled far up, a picture zoomed into. Only the part of it the frame needs has to be drawn.
[TestFixture]
public sealed class LayerRequiredRegionTests
{
    private static readonly PixelSize s_frame = new(200, 120);

    // Far enough that drawing the layer whole needs a buffer wider than the bound asserted below.
    private const float FarOffset = 4000;

    [Test]
    public void AFiniteLayerFarLargerThanTheFrame_AllocatesOnlyWhatTheFrameNeeds()
    {
        static RenderNode Create(bool far)
        {
            FilterEffectRenderNode blur = CreateBlurNode(sigma: 3);
            var layer = new LayerRenderNode(new Rect(-40, -40, (far ? FarOffset + 100 : 0) + 300, 200));
            AddContent(layer, far);
            blur.AddChild(layer);
            return blur;
        }

        AssertFarContentCostsNothing(Create);
    }

    [Test]
    public void AContentIsolationFarLargerThanTheFrame_AllocatesOnlyWhatTheFrameNeeds()
    {
        static RenderNode Create(bool far)
        {
            // A scaled group isolates its content in a target layer scope that spans all of that content.
            var transform = new TransformRenderNode(
                Matrix.CreateTranslation(-100, -60)
                * Matrix.CreateScale(1.015f, 1.015f)
                * Matrix.CreateTranslation(100, 60),
                TransformOperator.Prepend);
            var isolation = new ContentIsolationRenderNode(isolatesContent: true);
            AddContent(isolation, far);
            transform.AddChild(isolation);
            return transform;
        }

        AssertFarContentCostsNothing(Create);
    }

    [TestCase(0.0)]
    [TestCase(0.4)]
    [TestCase(1.7)]
    public void AShakeOverALayerFarLargerThanTheFrame_AllocatesOnlyWhatTheFrameNeeds(double seconds)
    {
        // A camera shake moves what it is given by at most its strength, so it reads only that much past the frame.
        var shake = new ShakeEffect();
        shake.Id.Value = new Guid("00000000-0000-0000-0000-00000000c0de");
        shake.StrengthX.Value = 12;
        shake.StrengthY.Value = 9;
        var context = new CompositionContext(MediaTime.FromSeconds(seconds));

        RenderNode Create(bool far)
        {
            var node = new FilterEffectRenderNode(shake.ToResource(context));
            node.AddChild(CreateLayer(far));
            return node;
        }

        AssertFarContentCostsNothing(Create);

        // Unless the shake moves the content, the comparison above would not show that it moves it correctly.
        using Bitmap still = Render(CreateLayer(far: false), []);
        using Bitmap shaken = Render(Create(far: false), []);
        Assert.That(shaken.GetPixelSpan().ToArray(), Is.Not.EqualTo(still.GetPixelSpan().ToArray()));

        static LayerRenderNode CreateLayer(bool far)
        {
            var layer = new LayerRenderNode(new Rect(-40, -40, (far ? FarOffset + 100 : 0) + 300, 200));
            AddContent(layer, far);
            return layer;
        }
    }

    [Test]
    public void ALayerReadBackPastTheFrame_KeepsWhatTheReadbackCovers()
    {
        // A readback command snapshots all of the region it declares, however little of the layer the frame needs.
        var readback = new ReadbackNode(new Rect(FarOffset, 30, 50, 50));
        var layer = new LayerRenderNode(new Rect(-40, -40, FarOffset + 400, 200));
        AddContent(layer, far: true);
        layer.AddChild(readback);

        using Bitmap frame = Render(layer, []);

        Assert.That(readback.ReadbackSize, Is.EqualTo(new PixelSize(50, 50)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ACustomEffectGivenPartOfALayer_HandsOnWhatTheWholeLayerWould(bool blurAfter)
    {
        // A script that shades by its width and height reads the complete extent of what it is given, and it shades
        // where the layer has no content too. A custom effect given only the part of a layer the frame needs has to hand
        // on that extent, through whatever else the effect does after it, and a part that holds nothing where the layer
        // has nothing.
        var partSizes = new List<PixelSize>();
        var wholeSizes = new List<PixelSize>();
        using Bitmap part = Render(Create(declaresWhereItReads: true, blurAfter), partSizes);
        using Bitmap whole = Render(Create(declaresWhereItReads: false, blurAfter), wholeSizes);

        Assert.Multiple(() =>
        {
            Assert.That(part.GetPixelSpan().ToArray(), Is.EqualTo(whole.GetPixelSpan().ToArray()));
            Assert.That(wholeSizes.Max(static size => Math.Max(size.Width, size.Height)), Is.GreaterThan(512),
                "a custom effect that does not say where it reads must be given the whole layer");
            Assert.That(partSizes.Select(static size => Math.Max(size.Width, size.Height)),
                Is.All.LessThanOrEqualTo(512));
        });

        static RenderNode Create(bool declaresWhereItReads, bool blurAfter)
        {
            var script = new SKSLScriptEffect();
            script.Script.Value = """
                uniform shader src;
                uniform float width;
                uniform float height;
                half4 main(float2 c) {
                    float2 uv = c / float2(width, height) - 0.5;
                    return src.eval(c) * half(1.0 - length(uv)) + half4(0.0, 0.0, 0.1, 0.1);
                }
                """;
            var shade = new FilterEffectRenderNode(script.ToResource(CompositionContext.Default));
            var move = new FilterEffectRenderNode(
                new FixedOffsetEffect
                {
                    // A move by a fraction of a pixel, which leaves the part a request needs between pixels too.
                    Offset = new Vector(7.25f, -5.5f),
                    DeclaresWhereItReads = declaresWhereItReads,
                    // A filter in the same effect after the move widens the extent the script shades by.
                    BlurAfter = blurAfter ? new Size(2, 2) : null,
                }.ToResource(CompositionContext.Default));
            var layer = new LayerRenderNode(new Rect(-40, -40, FarOffset + 400, 200));
            AddContent(layer, far: true);
            move.AddChild(layer);
            shade.AddChild(move);
            return shade;
        }
    }

    [Test]
    public void ACustomEffectOverValuesItReadsNothingOf_MovesTheOthersAsTheWholeWould()
    {
        // A callback that moves each target by its own amount tells its targets apart by their place, so a value the
        // frame needs nothing of still has to reach it, or every value after it would move as the one before it.
        var partSizes = new List<PixelSize>();
        var wholeSizes = new List<PixelSize>();
        using Bitmap part = Render(Create(declaresWhereItReads: true), partSizes);
        using Bitmap whole = Render(Create(declaresWhereItReads: false), wholeSizes);

        Assert.Multiple(() =>
        {
            Assert.That(part.GetPixelSpan().ToArray(), Is.EqualTo(whole.GetPixelSpan().ToArray()));
            Assert.That(wholeSizes.Max(static size => Math.Max(size.Width, size.Height)), Is.GreaterThan(512),
                "a custom effect that does not say where it reads must be given every value whole");
            Assert.That(partSizes.Select(static size => Math.Max(size.Width, size.Height)),
                Is.All.LessThanOrEqualTo(512));
        });

        static RenderNode Create(bool declaresWhereItReads)
        {
            var move = new FilterEffectRenderNode(
                new FixedOffsetEffect
                {
                    Offset = new Vector(7.25f, -5.5f),
                    MovesEachFurther = true,
                    DeclaresWhereItReads = declaresWhereItReads,
                }.ToResource(CompositionContext.Default));
            move.AddChild(new RectangleRenderNode(new Rect(FarOffset, 30, 2000, 50), Brushes.Resource.White, null));
            move.AddChild(new RectangleRenderNode(new Rect(20, 20, 60, 40), Brushes.Resource.White, null));
            move.AddChild(new RectangleRenderNode(new Rect(100, 60, 40, 30), Brushes.Resource.White, null));
            return move;
        }
    }

    private static void AssertFarContentCostsNothing(Func<bool, RenderNode> create)
    {
        var nearSizes = new List<PixelSize>();
        var farSizes = new List<PixelSize>();
        using Bitmap near = Render(create(false), nearSizes);
        using Bitmap far = Render(create(true), farSizes);

        Assert.Multiple(() =>
        {
            Assert.That(farSizes, Is.Not.Empty, "the fixture must materialize the layer");
            Assert.That(far.GetPixelSpan().ToArray(), Is.EqualTo(near.GetPixelSpan().ToArray()),
                "content outside the frame must not change what the frame shows");
            Assert.That(farSizes.Select(static size => Math.Max(size.Width, size.Height)),
                Is.All.LessThanOrEqualTo(512),
                "no intermediate may reach the far content's extent");
        });
    }

    private static void AddContent(ContainerRenderNode layer, bool far)
    {
        layer.AddChild(new RectangleRenderNode(new Rect(20, 20, 60, 40), Brushes.Resource.White, null));
        // Content just past the frame keeps the layer's own edge out of sight in both renders, so the frame compares
        // what the far content adds rather than how a buffer's edge is sampled.
        layer.AddChild(new RectangleRenderNode(new Rect(230, 90, 20, 20), Brushes.Resource.White, null));
        var rotated = new TransformRenderNode(Matrix.CreateRotation(MathF.PI / 7) * Matrix.CreateTranslation(130, 30),
            TransformOperator.Prepend);
        rotated.AddChild(new RectangleRenderNode(new Rect(0, 0, 50, 30), Brushes.Resource.White, null));
        layer.AddChild(rotated);
        if (far)
            layer.AddChild(new RectangleRenderNode(new Rect(FarOffset, 30, 50, 50), Brushes.Resource.White, null));
    }

    private static Bitmap Render(RenderNode root, List<PixelSize> requestedSizes)
    {
        using RenderNode ownedRoot = root;
        using var destination = new CpuRenderTarget(s_frame);
        using var canvas = new ImmediateCanvas(destination, RenderIntent.Delivery, logicalSize: s_frame.ToSize(1));
        using var renderer = new RenderNodeRenderer(ownedRoot, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Delivery,
            TargetDomain = new Rect(default, s_frame.ToSize(1)),
            OutputScale = 1,
            MaxWorkingScale = 1,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            Purpose = RenderRequestPurpose.Frame,
        }, new RecordingCpuTargetFactory(requestedSizes));

        renderer.Render(canvas);
        return destination.Snapshot();
    }

    private static FilterEffectRenderNode CreateBlurNode(float sigma)
    {
        var blur = new Blur();
        blur.Sigma.Value = new Size(sigma, sigma);
        return new FilterEffectRenderNode(blur.ToResource(CompositionContext.Default));
    }

    // Snapshots its target over a region, as an effect that reads back what was drawn before it does.
    private sealed class ReadbackNode(Rect region) : RenderNode
    {
        public PixelSize? ReadbackSize { get; private set; }

        public override void Process(RenderNodeContext context)
            => context.Publish(context.TargetCommand(
                [],
                TargetCommandDescription.CreateRequestLocal(
                    session => session.UseSnapshot(
                        bitmap => ReadbackSize = new PixelSize(bitmap.Width, bitmap.Height)),
                    TargetRegion.Region(region),
                    Rect.Empty,
                    RenderHitTestContract.None,
                    TargetAccess.Readback)));
    }

    private sealed class RecordingCpuTargetFactory(List<PixelSize> requestedSizes) : IRenderTargetFactory
    {
        public RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
        {
            requestedSizes.Add(allocation.DeviceSize);
            return new CpuRenderTarget(allocation.DeviceSize);
        }
    }
}

// Moves every target by a fixed offset, as a shake does at one moment, and says where it reads or leaves it unknown.
[SuppressResourceClassGeneration]
internal sealed class FixedOffsetEffect : FilterEffect
{
    public Vector Offset { get; init; }

    public bool DeclaresWhereItReads { get; init; }

    // Moves each target one offset further than the one before it.
    public bool MovesEachFurther { get; init; }

    public Size? BlurAfter { get; init; }

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool versionBumped = false;
        resource.Reconcile(this, context, ref versionBumped);
        return resource;
    }

    public new sealed class Resource : FilterEffect.Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            var effect = (FixedOffsetEffect)RequireOriginal();
            Action<(Vector Offset, bool Further), CustomFilterEffectContext> move = static (data, effectContext) =>
                effectContext.ForEach((index, target) =>
                    target.Bounds = target.Bounds.Translate(data.Offset * (data.Further ? index + 1 : 1)));
            if (effect.DeclaresWhereItReads)
            {
                context.CustomEffectWithSamplingBounds(
                    (Offset: effect.Offset, Further: effect.MovesEachFurther),
                    move,
                    static (data, output) =>
                    {
                        // From the nearest move to the furthest of three targets'.
                        Vector reach = data.Offset * (data.Further ? 3 : 1);
                        return output.Translate(-data.Offset).Union(output.Translate(-reach));
                    });
            }
            else
            {
                context.CustomEffect((Offset: effect.Offset, Further: effect.MovesEachFurther), move);
            }

            if (effect.BlurAfter is { } sigma)
                context.Blur(sigma);
        }
    }
}
