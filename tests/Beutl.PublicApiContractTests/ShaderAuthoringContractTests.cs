using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class ShaderAuthoringContractTests
{
    private const string IdentityCurrentPixel = "half4 apply(half4 color) { return color; }";
    private const string IdentityWholeSource =
        "uniform shader src; half4 main(float2 coord) { return src.eval(coord); }";

    [Test]
    public void CurrentPixelAndWholeSource_ExposeDistinctPublicBoundsAndSourceContracts()
    {
        ShaderDescription currentPixel = ShaderDescription.CurrentPixel(
            "uniform float amount; half4 apply(half4 color) { return color * amount; }",
            bindings => bindings.Uniform("amount", 0.75f));
        RenderBoundsContract bounds = RenderBoundsContract.Create(
            static input => input.Inflate(new Thickness(2, 3, 4, 5)),
            static output => output.Inflate(new Thickness(4, 5, 2, 3)),
            structuralKey: "asymmetric-shader-bounds");
        ShaderDescription wholeSource = ShaderDescription.WholeSource(
            IdentityWholeSource,
            bounds,
            sourceTileMode: SKShaderTileMode.Clamp);
        var input = new Rect(10, 20, 30, 40);
        var requested = new Rect(12, 24, 8, 9);

        Assert.Multiple(() =>
        {
            Assert.That(currentPixel.Kind, Is.EqualTo(ShaderDescriptionKind.CurrentPixel));
            Assert.That(currentPixel.Bounds, Is.EqualTo(RenderBoundsContract.Identity));
            Assert.That(currentPixel.Uniforms.Select(static item => item.Name), Is.EqualTo(new[] { "amount" }));
            Assert.That(currentPixel.Resources, Is.Empty);
            Assert.That(currentPixel.Source.Kind, Is.EqualTo(ShaderDescriptionKind.CurrentPixel));
            Assert.That(currentPixel.Source.IdentityHash, Is.Not.Empty);

            Assert.That(wholeSource.Kind, Is.EqualTo(ShaderDescriptionKind.WholeSource));
            Assert.That(wholeSource.Source.Kind, Is.EqualTo(ShaderDescriptionKind.WholeSource));
            Assert.That(wholeSource.SourceTileMode, Is.EqualTo(SKShaderTileMode.Clamp));
            Assert.That(wholeSource.Bounds.TransformBounds(input),
                Is.EqualTo(input.Inflate(new Thickness(2, 3, 4, 5))));
            Assert.That(wholeSource.Bounds.GetRequiredInputBounds(requested),
                Is.EqualTo(requested.Inflate(new Thickness(4, 5, 2, 3))));
        });
    }

    [Test]
    public void ShaderOpacityShaderChain_PreservesValueEligibilityAcrossDistinctNodes()
    {
        var snapshots = new Dictionary<string, FragmentSnapshot>();
        var bounds = new Rect(4, 7, 11, 9);
        var first = new ShaderMapNode(
            ShaderDescription.CurrentPixel(IdentityCurrentPixel),
            fragment => snapshots["first-shader"] = FragmentSnapshot.From(fragment));
        first.AddChild(new MetadataSourceNode(bounds));
        var opacity = new OpacityMapNode(
            0.5f,
            fragment => snapshots["opacity"] = FragmentSnapshot.From(fragment));
        opacity.AddChild(first);
        var second = new ShaderMapNode(
            ShaderDescription.CurrentPixel(IdentityCurrentPixel),
            fragment => snapshots["second-shader"] = FragmentSnapshot.From(fragment));
        second.AddChild(opacity);
        var whole = new ShaderMapNode(
            ShaderDescription.WholeSource(IdentityWholeSource, RenderBoundsContract.Identity),
            fragment => snapshots["whole-source"] = FragmentSnapshot.From(fragment));
        whole.AddChild(second);

        using (whole)
        using (var renderer = CreateRenderer(whole))
        {
            RenderNodeMeasurement measurement = renderer.Measure();
            Assert.Multiple(() =>
            {
                Assert.That(measurement.ValueCardinality, Is.EqualTo(RenderValueCardinality.Single));
                Assert.That(measurement.OutputBounds, Is.EqualTo(bounds));
                Assert.That(measurement.HasContributingValues, Is.True);

                Assert.That(snapshots["first-shader"].CanBeUsedAsValueInput, Is.True);
                Assert.That(snapshots["opacity"].CanBeUsedAsValueInput, Is.True);
                Assert.That(snapshots["second-shader"].CanBeUsedAsValueInput, Is.True);
                Assert.That(snapshots["whole-source"].CanBeUsedAsValueInput, Is.True);
                Assert.That(snapshots["first-shader"].EffectiveScale, Is.EqualTo(EffectiveScale.Unbounded));
                Assert.That(snapshots["opacity"].EffectiveScale, Is.EqualTo(EffectiveScale.Unbounded));
                Assert.That(snapshots["whole-source"].EffectiveScale, Is.EqualTo(EffectiveScale.At(1)));
            });
        }
    }

    [Test]
    public void Shader_RejectsCommandAndTargetScopeInputsWithoutImplicitMaterialization()
    {
        var bounds = new Rect(0, 0, 8, 6);
        ShaderDescription description = ShaderDescription.CurrentPixel(IdentityCurrentPixel);
        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(MetadataSource(bounds));
            RenderFragmentHandle command = context.TargetCommand(
                [],
                TargetCommandDescription.Create(
                    static _ => throw new AssertionException("A metadata request must not execute the command."),
                    TargetRegion.Region(bounds),
                    Rect.Empty,
                    RenderHitTestContract.None,
                    TargetAccess.ReadWrite,
                    structuralKey: "shader-ineligible-command"));
            RenderFragmentHandle scope = context.TargetLayerScope([source, command], TargetRegion.Region(bounds));

            Assert.Multiple(() =>
            {
                Assert.That(command.CanBeUsedAsValueInput, Is.False);
                Assert.That(scope.CanBeUsedAsValueInput, Is.False);
                Assert.That(() => context.Shader(command, description), Throws.TypeOf<ArgumentException>());
                Assert.That(() => context.Shader(scope, description), Throws.TypeOf<ArgumentException>());
            });

            context.Publish(source);
        });

        using var renderer = CreateRenderer(node, targetDomain: bounds);
        Assert.That(renderer.Measure().HasContributingValues, Is.True);
    }

    [Test]
    public void FilterEffectShader_BindsUniformsAndResourcesInOrderOnTheCpuFallback()
    {
        var bounds = new Rect(0, 0, 8, 6);
        var executionOrder = new List<string>();
        var color = new ShaderColor(SKColors.CornflowerBlue);
        var effect = new PluginShaderEffect(executionOrder, color);
        using PluginShaderEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using FilterEffectRenderNode node = resource.CreateRenderNode();
        node.AddChild(new SolidSourceNode(bounds, Colors.OrangeRed));

        using var renderer = CreateRenderer(node);
        RenderNodeMeasurement measurement = renderer.Measure();
        Assert.That(executionOrder, Is.Empty,
            "Measure and ApplyTo recording must not invoke shader binders.");

        using RenderNodeRasterization rasterization = renderer.Rasterize();
        using SKShader rejectedShader = SKShader.CreateColor(SKColors.White);

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(rasterization.Bitmap, Is.Not.Null);
            Assert.That(MaxAlpha(rasterization.Bitmap!), Is.EqualTo(0.8125f).Within(0.02f),
                "The scalar custom uniforms must be bound as scalars and applied in authored order.");
            Assert.That(executionOrder, Is.EqualTo(new[]
            {
                "current-uniform",
                "whole-uniform",
                "whole-resource",
            }));
            Assert.That(effect.UniformPurpose, Is.EqualTo(RenderRequestPurpose.Auxiliary));
            Assert.That(effect.UniformOutputBounds, Is.EqualTo(bounds));
            Assert.That(effect.ResourceDeviceSize, Is.EqualTo(new PixelSize(8, 6)));
            Assert.That(effect.ProducedShader, Is.Not.Null);
            Assert.That(effect.ProducedShader!.Handle, Is.EqualTo(IntPtr.Zero),
                "The shader returned by a resource binder transfers to and is disposed by the engine.");
            Assert.That(() => effect.RetainedUniformWriter!.Set(1f), Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => effect.RetainedResourceWriter!.Set(rejectedShader),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => _ = effect.UniformContext!.Purpose,
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => _ = effect.ResourceContext!.DeviceSize,
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(color.DisposeCalls, Is.Zero, "Borrow does not transfer resource ownership.");
        });
    }

    [Test]
    public void WholeSource_BindsDeclaredResourceOnTheCpuFallbackAndTransfersShaderOwnership()
    {
        var bounds = new Rect(0, 0, 5, 4);
        var color = new ShaderColor(SKColors.MediumPurple);
        ShaderResourceWriter? retainedWriter = null;
        ShaderExecutionContext? executionContext = null;
        SKShader? transferredShader = null;
        Rect observedOutputBounds = default;
        PixelSize observedDeviceSize = default;
        int binderCalls = 0;
        using var node = new DelegateNode(context =>
        {
            RenderResource<ShaderColor> token = context.Borrow(color, "whole-source-resource", version: 3);
            ShaderDescription description = ShaderDescription.WholeSource(
                "uniform shader src; uniform shader tint; "
                + "half4 main(float2 coord) { return mix(src.eval(coord), tint.eval(coord), 0.25); }",
                RenderBoundsContract.Identity,
                bindings => bindings.Resource(
                    "tint",
                    token,
                    ShaderResourceCoordinateSpace.OutputDevice,
                    (writer, value, execution) =>
                    {
                        binderCalls++;
                        retainedWriter = writer;
                        executionContext = execution;
                        observedOutputBounds = execution.OutputBounds;
                        observedDeviceSize = execution.DeviceSize;
                        transferredShader = SKShader.CreateColor(value.Color);
                        writer.Set(transferredShader);
                    },
                    structuralKey: "whole-source-resource-binder",
                    runtimeIdentity: new RenderRuntimeIdentity("whole-source-resource-runtime")));
            RenderFragmentHandle source = context.OpaqueSource(ExecutingSource(bounds, Colors.White));
            context.Publish(context.Shader(source, description));
        });

        using RenderNodeRasterization rasterization = Rasterize(node, outputScale: 2);
        using SKShader rejectedShader = SKShader.CreateColor(SKColors.White);

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(MaxAlpha(rasterization.Bitmap!), Is.GreaterThan(0.9f));
            Assert.That(binderCalls, Is.EqualTo(1));
            Assert.That(observedOutputBounds, Is.EqualTo(bounds));
            Assert.That(observedDeviceSize, Is.EqualTo(new PixelSize(10, 8)));
            Assert.That(
                () => _ = executionContext!.OutputBounds,
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(transferredShader, Is.Not.Null);
            Assert.That(transferredShader!.Handle, Is.EqualTo(IntPtr.Zero));
            Assert.That(() => retainedWriter!.Set(rejectedShader), Throws.TypeOf<InvalidOperationException>());
            Assert.That(color.DisposeCalls, Is.Zero);
        });
    }

    [Test]
    public void NonlinearCurrentPixel_RunsAfterAnalyticAntialiasedCoverageIsResolved()
    {
        var bounds = new Rect(0.35f, 0.2f, 9.3f, 7.6f);
        using var baseline = new EllipseRenderNode(bounds, Brushes.Resource.White, null);
        using var shaded = new ShaderMapNode(ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return color * color.a; }"));
        shaded.AddChild(new EllipseRenderNode(bounds, Brushes.Resource.White, null));
        using RenderNodeRasterization baselinePixels = Rasterize(baseline, outputScale: 4);
        using RenderNodeRasterization shadedPixels = Rasterize(shaded, outputScale: 4);

        float[] before = ReadAlpha(baselinePixels.Bitmap!);
        float[] after = ReadAlpha(shadedPixels.Bitmap!);
        var partial = before
            .Select((alpha, index) => (alpha, index))
            .Where(static item => item.alpha is > 0.05f and < 0.95f)
            .ToArray();
        double squaredError = partial.Average(item => Math.Abs(after[item.index] - item.alpha * item.alpha));
        double preCoverageError = partial.Average(item => Math.Abs(after[item.index] - item.alpha));

        Assert.Multiple(() =>
        {
            Assert.That(baselinePixels.Bounds, Is.EqualTo(shadedPixels.Bounds));
            Assert.That(partial, Has.Length.GreaterThan(4), "The control must contain antialiased edge pixels.");
            Assert.That(squaredError, Is.LessThan(preCoverageError * 0.85),
                "The shaded edge must be materially closer to post-coverage a² than unchanged coverage a.");
            Assert.That(preCoverageError - squaredError, Is.GreaterThan(0.015),
                "Applying the shader before analytic coverage would leave edge alpha near a.");
        });
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode node, Rect? targetDomain = null)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                TargetDomain = targetDomain,
                OutputScale = 1,
                MaxWorkingScale = 4,
                UseRenderCache = false,
                TargetFactory = new CpuTargetFactory(),
            });

    private static RenderNodeRasterization Rasterize(RenderNode node, float outputScale)
    {
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                OutputScale = outputScale,
                MaxWorkingScale = outputScale,
                UseRenderCache = false,
                TargetFactory = new CpuTargetFactory(),
            });
        return renderer.Rasterize();
    }

    private static float[] ReadAlpha(Bitmap bitmap)
    {
        Assert.That(bitmap.ColorType, Is.EqualTo(BitmapColorType.RgbaF16));
        var result = new float[bitmap.Width * bitmap.Height];
        for (int y = 0; y < bitmap.Height; y++)
        {
            Span<Half> row = bitmap.GetRow<Half>(y);
            for (int x = 0; x < bitmap.Width; x++)
                result[y * bitmap.Width + x] = (float)row[x * 4 + 3];
        }

        return result;
    }

    private static float MaxAlpha(Bitmap bitmap) => ReadAlpha(bitmap).Max();

    private static OpaqueRenderDescription MetadataSource(Rect bounds)
    {
        return OpaqueRenderDescription.Create(
            static _ => throw new AssertionException("A metadata request must not execute the source."),
            RenderOperationBoundsContract.Source(bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.Vector,
            structuralKey: (typeof(ShaderAuthoringContractTests), "metadata-source", bounds));
    }

    private static OpaqueRenderDescription ExecutingSource(Rect bounds, Color color)
    {
        return OpaqueRenderDescription.Create(
            session =>
            {
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                output.Canvas.Use(canvas => canvas.Clear(color));
                session.Publish(output);
            },
            RenderOperationBoundsContract.Source(bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale,
            structuralKey: (typeof(ShaderAuthoringContractTests), "executing-source"),
            runtimeIdentity: new RenderRuntimeIdentity((bounds, color)));
    }

    private sealed class MetadataSourceNode(Rect bounds) : RenderNode
    {
        public override void Process(RenderNodeContext context)
            => context.Publish(context.OpaqueSource(MetadataSource(bounds)));
    }

    private sealed class SolidSourceNode(Rect bounds, Color color) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.OpaqueSource(ExecutingSource(bounds, color)));
        }
    }

    private sealed class ShaderMapNode(
        ShaderDescription description,
        Action<RenderFragmentHandle>? observe = null) : ContainerRenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            foreach (RenderFragmentHandle input in context.Inputs)
            {
                RenderFragmentHandle output = context.Shader(input, description);
                observe?.Invoke(output);
                context.Publish(output);
            }
        }
    }

    private sealed class OpacityMapNode(float opacity, Action<RenderFragmentHandle> observe) : ContainerRenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            foreach (RenderFragmentHandle input in context.Inputs)
            {
                RenderFragmentHandle output = context.Opacity(input, opacity);
                observe(output);
                context.Publish(output);
            }
        }
    }

    private sealed class DelegateNode(Action<RenderNodeContext> process) : RenderNode
    {
        public override void Process(RenderNodeContext context) => process(context);
    }

    [SuppressResourceClassGeneration]
    private sealed partial class PluginShaderEffect(
        List<string> executionOrder,
        ShaderColor color) : FilterEffect
    {
        public ShaderUniformWriter? RetainedUniformWriter { get; private set; }

        public ShaderResourceWriter? RetainedResourceWriter { get; private set; }

        public ShaderExecutionContext? UniformContext { get; private set; }

        public ShaderExecutionContext? ResourceContext { get; private set; }

        public RenderRequestPurpose UniformPurpose { get; private set; }

        public Rect UniformOutputBounds { get; private set; }

        public PixelSize ResourceDeviceSize { get; private set; }

        public SKShader? ProducedShader { get; private set; }

        public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
        {
            context.Shader(ShaderDescription.CurrentPixel(
                "uniform float gain; half4 apply(half4 color) { return color * gain; }",
                bindings => bindings.Uniform(
                    "gain",
                    0.75f,
                    (writer, value, execution) =>
                    {
                        executionOrder.Add("current-uniform");
                        RetainedUniformWriter = writer;
                        UniformContext = execution;
                        UniformPurpose = execution.Purpose;
                        UniformOutputBounds = execution.OutputBounds;
                        writer.Set(value);
                    },
                    structuralKey: "current-gain",
                    runtimeIdentity: new RenderRuntimeIdentity("current-gain-runtime"))));

            RenderResource<ShaderColor> colorToken = context.Borrow(color, "shader-color", version: 1);
            context.Shader(ShaderDescription.WholeSource(
                "uniform shader src; uniform shader tint; uniform float amount; "
                + "half4 main(float2 coord) { return mix(src.eval(coord), tint.eval(coord), amount); }",
                RenderBoundsContract.Identity,
                bindings =>
                {
                    bindings.Uniform(
                        "amount",
                        0.25f,
                        (writer, value, execution) =>
                        {
                            executionOrder.Add("whole-uniform");
                            UniformContext = execution;
                            writer.Set(value);
                        },
                        structuralKey: "whole-amount",
                        runtimeIdentity: new RenderRuntimeIdentity("whole-amount-runtime"));
                    bindings.Resource(
                        "tint",
                        colorToken,
                        ShaderResourceCoordinateSpace.OutputDevice,
                        (writer, value, execution) =>
                        {
                            executionOrder.Add("whole-resource");
                            RetainedResourceWriter = writer;
                            ResourceContext = execution;
                            ResourceDeviceSize = execution.DeviceSize;
                            ProducedShader = SKShader.CreateColor(value.Color);
                            writer.Set(ProducedShader);
                        },
                        structuralKey: "whole-tint",
                        runtimeIdentity: new RenderRuntimeIdentity("whole-tint-runtime"));
                }));
        }

        public override Resource ToResource(CompositionContext context)
        {
            var resource = new Resource();
            bool updateOnly = false;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }

        public new sealed class Resource : FilterEffect.Resource
        {
            public override FilterEffectRenderNode CreateRenderNode() => new(this);
        }
    }

    private sealed class ShaderColor(SKColor color) : IDisposable
    {
        public SKColor Color { get; } = color;

        public int DisposeCalls { get; private set; }

        public void Dispose() => DisposeCalls++;
    }

    private readonly record struct FragmentSnapshot(
        EffectiveScale EffectiveScale,
        RenderValueCardinality Cardinality,
        bool ContributesValues,
        bool CanBeUsedAsValueInput)
    {
        public static FragmentSnapshot From(RenderFragmentHandle handle)
        {
            Assert.That(handle.TryGetMetadata(out RenderFragmentMetadata metadata), Is.True);
            return new FragmentSnapshot(
                metadata.EffectiveScale,
                handle.ValueCardinality,
                handle.ContributesValuesToTarget,
                handle.CanBeUsedAsValueInput);
        }
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(PixelSize deviceSize) => new CpuRenderTarget(deviceSize);
    }

    private sealed class CpuRenderTarget : RenderTarget
    {
        private static readonly SKColorSpace s_colorSpace = SKColorSpace.CreateSrgbLinear();

        public CpuRenderTarget(PixelSize size)
            : base(CreateSurface(size), size.Width, size.Height)
        {
        }

        private static SKSurface CreateSurface(PixelSize size)
        {
            return SKSurface.Create(new SKImageInfo(
                       size.Width,
                       size.Height,
                       SKColorType.RgbaF16,
                       SKAlphaType.Premul,
                       s_colorSpace))
                   ?? throw new InvalidOperationException("Could not create a CPU contract-test surface.");
        }
    }
}
