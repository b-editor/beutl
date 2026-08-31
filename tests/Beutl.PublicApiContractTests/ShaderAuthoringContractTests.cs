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
    private const string TwoChildWholeSource =
        "uniform shader src; uniform shader tintA; uniform shader tintB; "
        + "half4 main(float2 coord) { return (tintA.eval(coord) + tintB.eval(coord)) * 0.5; }";
    private const string TranslateWholeSource =
        "uniform shader src; uniform float dx; uniform float dy; "
        + "half4 main(float2 coord) { return src.eval(coord - float2(dx, dy)); }";
    private static readonly Rect s_bounds = new(0, 0, 8, 6);
    private static readonly Vector s_translation = new(20, 10);
    private static readonly RenderResourceSlot<ShaderColor> s_colorSlot = new();
    private static readonly RenderResourceSlot<ShaderColor> s_sharedColorSlot = new();
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
    private static readonly ShaderDefinition<Vector> s_relocatingDefinition =
        ShaderDefinition<Vector>.WholeSource(
            TranslateWholeSource,
            TranslateBounds(),
            DeclareTranslateUniforms,
            hitTest: RenderHitTestContract.Custom(
                s_translation,
                static (offset, context, point) => context.Inputs[0].HitTest(point - offset)));
    private static readonly ShaderDefinition<Vector> s_undeclaredRelocatingDefinition =
        ShaderDefinition<Vector>.WholeSource(
            TranslateWholeSource,
            TranslateBounds(),
            DeclareTranslateUniforms);

    /// <remarks>
    /// A plugin author with many effects over one shader has the same reason the engine does to parse it
    /// once. SkslSource, its Kind, and the definition factories that take one were public in name only:
    /// nothing reachable from outside the assembly could produce or consume an instance.
    /// </remarks>
    [Test]
    public void AParsedSourceCanBeSharedAcrossDefinitions()
    {
        SkslSource parsed = SkslSource.CurrentPixel(CurrentPixelSource);
        ShaderDefinition<float> first = ShaderDefinition<float>.CurrentPixel(
            parsed,
            static bindings => bindings.Uniform("amount", static state => state));
        ShaderDefinition<float> second = ShaderDefinition<float>.CurrentPixel(
            parsed,
            static bindings => bindings.Uniform("amount", static state => 1f - state));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed.Kind, Is.EqualTo(ShaderDescriptionKind.CurrentPixel));
            Assert.That(parsed.Text.TrimEnd(), Is.EqualTo(CurrentPixelSource), "the text is normalized, not rewritten");
            Assert.That(parsed.IdentityHash, Is.Not.Empty);
            Assert.That(first, Is.Not.SameAs(second));
        }
    }

    [Test]
    public void AParsedWholeSourceCanHeadADefinition()
    {
        SkslSource parsed = SkslSource.WholeSource(WholeSource);

        ShaderDefinition<byte> definition = ShaderDefinition<byte>.WholeSource(
            parsed,
            RenderBoundsContract.Identity,
            static bindings => bindings.Resource(
                "tint",
                s_colorSlot,
                ShaderResourceCoordinateSpace.OutputDevice,
                static (writer, color, _) => writer.Set(SKShader.CreateColor(color.Color))));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed.Kind, Is.EqualTo(ShaderDescriptionKind.WholeSource));
            Assert.That(definition, Is.Not.Null);
        }
    }

    [Test]
    public void AParsedSourceOfTheWrongKind_IsRejectedWhereItIsDeclared()
    {
        SkslSource currentPixel = SkslSource.CurrentPixel(CurrentPixelSource);

        Assert.That(
            () => ShaderDefinition<byte>.WholeSource(currentPixel, RenderBoundsContract.Identity),
            Throws.ArgumentException);
    }

    /// <remarks>
    /// A WholeSource shader's input arrives as the implicit 'src' child, so binding it explicitly is not
    /// something the pipeline can honour. Accepting the definition and throwing on every call of it hands the
    /// author a shape that builds and is then unusable, with nothing pointing at the declaration that did it.
    /// </remarks>
    [Test]
    public void AWholeSourceDefinitionBindingSrcExplicitly_IsRejectedWhereItIsDeclared()
    {
        Assert.That(
            () => ShaderDefinition<byte>.WholeSource(
                WholeSource,
                RenderBoundsContract.Identity,
                static bindings => bindings.Resource(
                    "src",
                    s_colorSlot,
                    ShaderResourceCoordinateSpace.OutputDevice,
                    static (writer, color, _) => writer.Set(SKShader.CreateColor(color.Color)))),
            Throws.ArgumentException.With.Message.Contains("implicit WholeSource input"));
    }

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

    /// <remarks>
    /// Two child-shader names legitimately read one resource - the same bitmap sampled through two call-state
    /// matrices is one leased resource and two bindings. A slot is the address a call binds, so declaring it
    /// once is what the call has to satisfy however many names read it.
    /// </remarks>
    [Test]
    public void TwoChildShaderBindings_CanShareOneResourceSlot()
    {
        var color = new ShaderColor(SKColors.MediumPurple);
        ShaderDefinition<float> definition = ShaderDefinition<float>.WholeSource(
            TwoChildWholeSource,
            RenderBoundsContract.Identity,
            static bindings =>
            {
                bindings.Resource(
                    "tintA",
                    s_sharedColorSlot,
                    ShaderResourceCoordinateSpace.OutputDevice,
                    static (writer, shared, _) =>
                    {
                        shared.Uses++;
                        writer.Set(SKShader.CreateColor(shared.Color));
                    });
                bindings.Resource(
                    "tintB",
                    s_sharedColorSlot,
                    ShaderResourceCoordinateSpace.OutputDevice,
                    static state => state,
                    static (writer, shared, opacity, _) =>
                    {
                        shared.Uses++;
                        shared.LastOpacity = opacity;
                        writer.Set(SKShader.CreateColor(shared.Color.WithAlpha((byte)(opacity * 255))));
                    });
            });

        using var node = new DelegateNode(context =>
        {
            RenderResource<ShaderColor> token = context.Borrow(color);
            RenderFragmentHandle source = context.OpaqueSource(SourceCall(Colors.White));
            context.Publish(context.Shader(
                source,
                definition.Call(0.5f, [s_sharedColorSlot.Bind(token)])));
        });

        using RenderNodeRasterization rasterization = Rasterize(node);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(color.Uses, Is.EqualTo(2), "both binding templates resolve the shared slot");
            Assert.That(color.LastOpacity, Is.EqualTo(0.5f), "the state-reading template keeps its own value");
        }
    }

    [Test]
    public void ACallSharingOneSlotAcrossBindings_BindsThatSlotExactlyOnce()
    {
        ShaderDefinition<float> definition = ShaderDefinition<float>.WholeSource(
            TwoChildWholeSource,
            RenderBoundsContract.Identity,
            static bindings =>
            {
                bindings.Resource(
                    "tintA",
                    s_sharedColorSlot,
                    ShaderResourceCoordinateSpace.OutputDevice,
                    static (writer, shared, _) => writer.Set(SKShader.CreateColor(shared.Color)));
                bindings.Resource(
                    "tintB",
                    s_sharedColorSlot,
                    ShaderResourceCoordinateSpace.OutputDevice,
                    static (writer, shared, _) => writer.Set(SKShader.CreateColor(shared.Color)));
            });

        var color = new ShaderColor(SKColors.MediumPurple);
        Exception? failure = null;
        using var node = new DelegateNode(context =>
        {
            RenderResource<ShaderColor> token = context.Borrow(color);
            RenderResourceBinding binding = s_sharedColorSlot.Bind(token);
            failure = Assert.Throws<ArgumentException>(() => definition.Call(0.5f, [binding, binding]));
            RenderFragmentHandle source = context.OpaqueSource(SourceCall(Colors.White));
            context.Publish(context.Shader(source, definition.Call(0.5f, [binding])));
        });

        using RenderNodeRasterization rasterization = Rasterize(node);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(failure, Is.Not.Null, "a shared slot is still bound once, not once per name");
        }
    }

    /// <remarks>
    /// A stage is authored either through a definition and a call, or by building the description directly.
    /// What the second route may not drag out with it is how a stage carries its bindings: the uniform and
    /// resource bindings a builder produces, and the Vulkan lowering an engine stage may attach, describe how
    /// the planner lowers and keys the stage rather than anything an author declares.
    /// <see cref="ShaderBindingBuilder"/> is the exception, because it is the parameter the author is handed.
    /// </remarks>
    [Test]
    public void ShaderDescription_IsAuthorableWithoutLeakingHowAStageCarriesItsBindings()
    {
        string?[] exportedTypes = typeof(RenderNode).Assembly
            .GetExportedTypes()
            .Select(static type => type.FullName)
            .ToArray();
        MethodInfo[] methods = typeof(RenderNodeContext)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(static method => method.Name == "Shader")
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(exportedTypes, Does.Contain("Beutl.Graphics.Effects.ShaderDescription"));
            Assert.That(exportedTypes, Does.Contain("Beutl.Graphics.Effects.ShaderBindingBuilder"));
            Assert.That(exportedTypes, Does.Not.Contain("Beutl.Graphics.Effects.ShaderUniformBinding"));
            Assert.That(exportedTypes, Does.Not.Contain("Beutl.Graphics.Effects.ShaderResourceBinding"));
            Assert.That(exportedTypes, Does.Not.Contain("Beutl.Graphics.Effects.SpirvShaderLowering"));
            Assert.That(methods, Has.Length.EqualTo(2));
            Assert.That(
                methods.Count(static method =>
                    method.IsGenericMethodDefinition
                    && method.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(ShaderCall<>)),
                Is.EqualTo(1));
            Assert.That(
                methods.Count(static method =>
                    !method.IsGenericMethodDefinition
                    && method.GetParameters()[1].ParameterType == typeof(ShaderDescription)),
                Is.EqualTo(1));
            Assert.That(
                typeof(ShaderDescription).GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(static method => method.Name is "CurrentPixel" or "WholeSource")
                    .All(static method => method.GetParameters().Any(static parameter =>
                        parameter.ParameterType == typeof(Action<ShaderBindingBuilder>))),
                Is.True,
                "the builder is how an author declares a stage's bindings, so it has to be reachable");
        });
    }


    /// <remarks>
    /// A whole-source stage states where its output lands in its bounds contract and puts it there with its own
    /// SkSL. Forwarding the hit test to the input asks about a point in the stage's own output space, which for
    /// a stage that moved its content names neither the pixels it produced nor the place it vacated. Only the
    /// author knows the inverse of the mapping their SkSL performs, so only the author can state the test.
    /// </remarks>
    [Test]
    public void AWholeSourceShaderThatRelocatesItsInput_HitsWhereItsDeclaredContractSays()
    {
        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(SourceCall(Colors.White));
            context.Publish(context.Shader(source, s_relocatingDefinition.Call(s_translation)));
        });

        using var renderer = CreateHitTestRenderer(node);
        RenderNodeMeasurement measurement = renderer.Measure();
        bool hitWhereTheContentMoved = renderer.HitTest(MovedPoint());
        bool hitWhereTheContentLeft = renderer.HitTest(VacatedPoint());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.OutputBounds, Is.EqualTo(s_bounds.Translate(s_translation)),
                "the bounds contract already says the content moved");
            Assert.That(hitWhereTheContentMoved, Is.True, "the produced pixels are here");
            Assert.That(hitWhereTheContentLeft, Is.False, "the original location is transparent now");
        }
    }

    /// <remarks>
    /// Declaring nothing has to keep meaning what it meant before this contract existed, or every shader
    /// already in the wild answers a different question than the one it was written against.
    /// </remarks>
    [Test]
    public void AWholeSourceShaderDeclaringNoHitTest_StillForwardsTheQuestionToItsInputUnchanged()
    {
        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(SourceCall(Colors.White));
            context.Publish(context.Shader(source, s_undeclaredRelocatingDefinition.Call(s_translation)));
        });

        using var renderer = CreateHitTestRenderer(node);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(renderer.HitTest(MovedPoint()), Is.False);
            Assert.That(renderer.HitTest(VacatedPoint()), Is.True);
        }
    }

    [Test]
    public void AWholeSourceHitTest_ReadsTheResourceItsOwnCallBound()
    {
        var color = new ShaderColor(SKColors.MediumPurple);
        ShaderDefinition<byte> definition = ShaderDefinition<byte>.WholeSource(
            WholeSource,
            RenderBoundsContract.Identity,
            static bindings => bindings.Resource(
                "tint",
                s_colorSlot,
                ShaderResourceCoordinateSpace.OutputDevice,
                static (writer, tint, _) => writer.Set(SKShader.CreateColor(tint.Color))),
            hitTest: RenderHitTestContract.FromSlot<ShaderColor>(
                s_colorSlot,
                static (tint, point) =>
                {
                    tint.HitTests++;
                    return tint.Color.Alpha > 0 && point.X < 4;
                }));

        using var node = new DelegateNode(context =>
        {
            RenderResource<ShaderColor> token = context.Borrow(color);
            RenderFragmentHandle source = context.OpaqueSource(SourceCall(Colors.White));
            context.Publish(context.Shader(source, definition.Call(default, [s_colorSlot.Bind(token)])));
        });

        using var renderer = CreateHitTestRenderer(node);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(renderer.HitTest(new Point(1, 1)), Is.True);
            Assert.That(renderer.HitTest(new Point(6, 1)), Is.False);
            Assert.That(color.HitTests, Is.EqualTo(2), "the hit test resolved the slot this call bound");
        }
    }

    [Test]
    public void AWholeSourceDefinitionDeclaringAnUninitializedHitTest_IsRejectedWhereItIsDeclared()
    {
        Assert.That(
            () => ShaderDefinition<Vector>.WholeSource(
                TranslateWholeSource,
                TranslateBounds(),
                DeclareTranslateUniforms,
                hitTest: default(RenderHitTestContract)),
            Throws.ArgumentException.With.Message.Contains("uninitialized"));
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


    private static Point MovedPoint() => new Point(4, 3) + s_translation;

    private static Point VacatedPoint() => new(4, 3);

    private static RenderBoundsContract TranslateBounds()
        => RenderBoundsContract.Create(
            s_translation,
            static (offset, bounds) => bounds.Translate(offset),
            static (offset, required) => required.Translate(-offset));

    private static void DeclareTranslateUniforms(ShaderDefinitionBuilder<Vector> bindings)
    {
        bindings.Uniform("dx", static offset => offset.X);
        bindings.Uniform("dy", static offset => offset.Y);
    }

    private static RenderNodeRenderer CreateHitTestRenderer(RenderNode node)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });

    private static RenderNodeMeasurement Measure(RenderNode node)
    {
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest { Intent = RenderIntent.Preview },
            });
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
                    Intent = RenderIntent.Preview,
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

        public int HitTests { get; set; }

        public float LastOpacity { get; set; }
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
