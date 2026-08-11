using System.Reflection;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class ShaderAuthoringContractTests
{
    private const string CurrentPixelSource =
        "uniform float amount; half4 apply(half4 color) { return color * amount; }";
    private const string WholeSource =
        "uniform shader src; uniform shader tint; half4 main(float2 coord) { return tint.eval(coord); }";
    private static readonly Rect s_bounds = new(0, 0, 8, 6);
    private static readonly RenderResourceSlot<ShaderColor> s_colorSlot = new();
    private static readonly ShaderDefinition<float> s_currentPixelDefinition =
        ShaderDefinition<float>.CurrentPixel(
            CurrentPixelSource,
            static bindings => bindings.Uniform("amount", static state => state));
    private static readonly ShaderDefinition<byte> s_wholeSourceDefinition =
        ShaderDefinition<byte>.WholeSource(
            WholeSource,
            RenderBoundsContract.Identity,
            static bindings => bindings.Resource(
                "tint",
                s_colorSlot,
                ShaderResourceCoordinateSpace.OutputDevice,
                static (writer, color, _) =>
                {
                    color.Uses++;
                    writer.Set(SKShader.CreateColor(color.Color));
                }));

    [Test]
    public void CurrentPixelDefinitionCall_MapsAValueEligibleInput()
    {
        ShaderCall<float> call = s_currentPixelDefinition.Call(0.75f);
        FragmentSnapshot output = default;
        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(SourceCall(Colors.White));
            RenderFragmentHandle shader = context.Shader(source, call);
            output = FragmentSnapshot.From(shader);
            context.Publish(shader);
        });

        RenderNodeMeasurement measurement = Measure(node);

        Assert.Multiple(() =>
        {
            Assert.That(call.Definition, Is.SameAs(s_currentPixelDefinition));
            Assert.That(call.State, Is.EqualTo(0.75f));
            Assert.That(output.Bounds, Is.EqualTo(s_bounds));
            Assert.That(output.CanBeUsedAsValueInput, Is.True);
            Assert.That(measurement.HasContributingValues, Is.True);
        });
    }

    [Test]
    public void WholeSourceDefinitionCall_UsesItsDeclaredTypedResourceSlot()
    {
        var color = new ShaderColor(SKColors.MediumPurple);
        using var node = new DelegateNode(context =>
        {
            RenderResource<ShaderColor> token = context.Borrow(color);
            RenderFragmentHandle source = context.OpaqueSource(SourceCall(Colors.White));
            context.Publish(context.Shader(
                source,
                s_wholeSourceDefinition.Call(default, [s_colorSlot.Bind(token)])));
        });

        using RenderNodeRasterization rasterization = Rasterize(node);

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(color.Uses, Is.EqualTo(1));
        });
    }

    [Test]
    public void ShaderDefinitions_RejectIncompleteAndDuplicateBindingShapes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                () => ShaderDefinition<byte>.CurrentPixel(CurrentPixelSource),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => ShaderDefinition<byte>.CurrentPixel(
                    CurrentPixelSource,
                    static bindings =>
                    {
                        bindings.Uniform("amount", static _ => 0.5f);
                        bindings.Uniform("amount", static _ => 0.75f);
                    }),
                Throws.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public void ShaderDefinitions_RejectCapturedUniformValueProviders()
    {
        float multiplier = 2;

        Assert.That(
            () => ShaderDefinition<float>.CurrentPixel(
                CurrentPixelSource,
                bindings => bindings.Uniform("amount", state => state * multiplier)),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ShaderDefinitions_RejectCapturedCustomUniformBinders()
    {
        float multiplier = 2;

        Assert.That(
            () => ShaderDefinition<float>.CurrentPixel(
                CurrentPixelSource,
                bindings => bindings.Uniform(
                    "amount",
                    static state => state,
                    (writer, value, _) => writer.Set(value * multiplier))),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ShaderDefinitions_RejectCapturedResourceBinders()
    {
        SKColor tint = SKColors.MediumPurple;

        Assert.That(
            () => ShaderDefinition<byte>.WholeSource(
                WholeSource,
                RenderBoundsContract.Identity,
                bindings => bindings.Resource(
                    "tint",
                    s_colorSlot,
                    ShaderResourceCoordinateSpace.OutputDevice,
                    (writer, _, _) => writer.Set(SKShader.CreateColor(tint)))),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ShaderDescription_IsNotPartOfTheExternalAuthoringSurface()
    {
        Assembly engine = typeof(RenderNode).Assembly;
        MethodInfo[] methods = typeof(RenderNodeContext)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(static method => method.Name == "Shader")
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(
                engine.GetExportedTypes().Any(static type => type.FullName == "Beutl.Graphics.Effects.ShaderDescription"),
                Is.False);
            Assert.That(methods, Has.Length.EqualTo(1));
            Assert.That(methods[0].GetParameters()[1].ParameterType.GetGenericTypeDefinition(),
                Is.EqualTo(typeof(ShaderCall<>)));
        });
    }

    private static OpaqueRenderCall<Color> SourceCall(Color color)
        => OpaqueRenderDefinition<Color>.Create(
            static (session, current) =>
            {
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                output.Canvas.Use(canvas => canvas.Clear(current));
                session.Publish(output);
            },
            OpaqueRenderBoundsContract.Source(s_bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale)
            .Call(color);

    private static RenderNodeMeasurement Measure(RenderNode node)
    {
        using var renderer = new RenderNodeRenderer(node);
        return renderer.Measure();
    }

    private static RenderNodeRasterization Rasterize(RenderNode node)
    {
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = new CpuTargetFactory(),
            });
        return renderer.Rasterize();
    }

    private sealed class DelegateNode(Action<RenderNodeContext> process) : RenderNode
    {
        public override void Process(RenderNodeContext context) => process(context);
    }

    private sealed class ShaderColor(SKColor color)
    {
        public SKColor Color { get; } = color;

        public int Uses { get; set; }
    }

    private readonly record struct FragmentSnapshot(Rect Bounds, bool CanBeUsedAsValueInput)
    {
        public static FragmentSnapshot From(RenderFragmentHandle handle)
        {
            Assert.That(handle.TryGetMetadata(out RenderFragmentMetadata metadata), Is.True);
            return new FragmentSnapshot(metadata.Bounds, handle.CanBeUsedAsValueInput);
        }
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public int GetMaximumDimension(RenderTargetAllocationDescriptor allocation)
            => RenderScaleUtilities.MaxBufferDimension;

        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
            => new CpuRenderTarget(allocation.DeviceSize);
    }

    private sealed class CpuRenderTarget : RenderTarget
    {
        private static readonly SKColorSpace s_colorSpace = SKColorSpace.CreateSrgbLinear();

        public CpuRenderTarget(PixelSize size)
            : base(
                SKSurface.Create(new SKImageInfo(
                    size.Width,
                    size.Height,
                    SKColorType.RgbaF16,
                    SKAlphaType.Premul,
                    s_colorSpace))
                ?? throw new InvalidOperationException("Could not create a CPU shader contract-test surface."),
                size.Width,
                size.Height)
        {
        }
    }
}
