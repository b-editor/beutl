using System.Reflection;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
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
    private static readonly ShaderDescription s_currentPixelDescription =
        ShaderDescription.CurrentPixel(
            CurrentPixelSource,
            static bindings => bindings.Uniform("amount", 0.75f));
    private static readonly ShaderDescription s_relocatingDescription =
        ShaderDescription.WholeSource(
            TranslateWholeSource,
            TranslateBounds(),
            DeclareTranslateUniforms,
            hitTest: RenderHitTestContract.Custom(
                s_translation,
                static (offset, context, point) => context.Inputs[0].HitTest(point - offset)));
    private static readonly ShaderDescription s_undeclaredRelocatingDescription =
        ShaderDescription.WholeSource(
            TranslateWholeSource,
            TranslateBounds(),
            DeclareTranslateUniforms);

    private static ShaderDescription WholeSourceTint(RenderResource<ShaderColor> token)
        => ShaderDescription.WholeSource(
            WholeSource,
            RenderBoundsContract.Identity,
            bindings => bindings.Resource(
                "tint",
                token,
                ShaderResourceCoordinateSpace.OutputDevice,
                static (writer, color, _) =>
                {
                    color.Uses++;
                    writer.Set(SKShader.CreateColor(color.Color));
                }));

    [Test]
    public void AParsedSourceCanBeSharedAcrossDescriptions()
    {
        SkslSource parsed = SkslSource.CurrentPixel(CurrentPixelSource);
        ShaderDescription first = ShaderDescription.CurrentPixel(
            parsed,
            static bindings => bindings.Uniform("amount", 0.5f));
        ShaderDescription second = ShaderDescription.CurrentPixel(
            parsed,
            static bindings => bindings.Uniform("amount", 0.25f));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed.Kind, Is.EqualTo(ShaderDescriptionKind.CurrentPixel));
            Assert.That(parsed.Text.TrimEnd(), Is.EqualTo(CurrentPixelSource), "the text is normalized, not rewritten");
            Assert.That(parsed.IdentityHash, Is.Not.Empty);
            Assert.That(first, Is.Not.SameAs(second));
        }
    }

    [Test]
    public void AParsedWholeSourceCanHeadADescription()
    {
        SkslSource parsed = SkslSource.WholeSource(WholeSource);
        ShaderDescription? description = null;

        using var node = new DelegateNode(context =>
        {
            RenderResource<ShaderColor> token = context.Borrow(new ShaderColor(SKColors.MediumPurple));
            description = ShaderDescription.WholeSource(
                parsed,
                RenderBoundsContract.Identity,
                bindings => bindings.Resource(
                    "tint",
                    token,
                    ShaderResourceCoordinateSpace.OutputDevice,
                    static (writer, color, _) => writer.Set(SKShader.CreateColor(color.Color))),
                SKShaderTileMode.Decal);
            context.Publish(context.OpaqueSource(SourceDescription(Colors.White)));
        });

        Measure(node);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed.Kind, Is.EqualTo(ShaderDescriptionKind.WholeSource));
            Assert.That(description, Is.Not.Null);
        }
    }

    [Test]
    public void AParsedSourceOfTheWrongKind_IsRejectedWhereItIsDeclared()
    {
        SkslSource currentPixel = SkslSource.CurrentPixel(CurrentPixelSource);

        Assert.That(
            () => ShaderDescription.WholeSource(
                currentPixel,
                RenderBoundsContract.Identity,
                bindings: null,
                SKShaderTileMode.Decal),
            Throws.ArgumentException);
    }

    [Test]
    public void AWholeSourceDescriptionBindingSrcExplicitly_IsRejectedWhereItIsDeclared()
    {
        Exception? failure = null;
        using var node = new DelegateNode(context =>
        {
            RenderResource<ShaderColor> token = context.Borrow(new ShaderColor(SKColors.MediumPurple));
            failure = Assert.Catch(() => ShaderDescription.WholeSource(
                WholeSource,
                RenderBoundsContract.Identity,
                bindings => bindings.Resource(
                    "src",
                    token,
                    ShaderResourceCoordinateSpace.OutputDevice,
                    static (writer, color, _) => writer.Set(SKShader.CreateColor(color.Color)))));
            context.Publish(context.OpaqueSource(SourceDescription(Colors.White)));
        });

        Measure(node);

        Assert.That(
            failure,
            Is.TypeOf<ArgumentException>().And.Message.Contains("implicit WholeSource input"));
    }

    [Test]
    public void ACurrentPixelDescription_MapsAValueEligibleInput()
    {
        FragmentSnapshot output = default;
        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(SourceDescription(Colors.White));
            RenderFragmentHandle shader = context.Shader(source, s_currentPixelDescription);
            output = FragmentSnapshot.From(shader);
            context.Publish(shader);
        });

        RenderNodeMeasurement measurement = Measure(node);

        Assert.Multiple(() =>
        {
            Assert.That(output.Bounds, Is.EqualTo(s_bounds));
            Assert.That(output.CanBeUsedAsValueInput, Is.True);
            Assert.That(measurement.HasContributingValues, Is.True);
        });
    }

    [Test]
    public void AWholeSourceDescription_UsesTheTypedResourceItDeclared()
    {
        var color = new ShaderColor(SKColors.MediumPurple);
        using var node = new DelegateNode(context =>
        {
            RenderResource<ShaderColor> token = context.Borrow(color);
            RenderFragmentHandle source = context.OpaqueSource(SourceDescription(Colors.White));
            context.Publish(context.Shader(source, WholeSourceTint(token)));
        });

        using RenderNodeRasterization rasterization = Rasterize(node);

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(color.Uses, Is.EqualTo(1));
        });
    }

    [Test]
    public void ShaderDescriptions_RejectIncompleteAndDuplicateBindingShapes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                () => ShaderDescription.CurrentPixel(CurrentPixelSource),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => ShaderDescription.CurrentPixel(
                    CurrentPixelSource,
                    static bindings =>
                    {
                        bindings.Uniform("amount", 0.5f);
                        bindings.Uniform("amount", 0.75f);
                    }),
                Throws.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public void TwoChildShaderBindings_CanShareOneResource()
    {
        var color = new ShaderColor(SKColors.MediumPurple);

        using var node = new DelegateNode(context =>
        {
            RenderResource<ShaderColor> token = context.Borrow(color);
            ShaderDescription description = ShaderDescription.WholeSource(
                TwoChildWholeSource,
                RenderBoundsContract.Identity,
                bindings =>
                {
                    bindings.Resource(
                        "tintA",
                        token,
                        ShaderResourceCoordinateSpace.OutputDevice,
                        static (writer, shared, _) =>
                        {
                            shared.Uses++;
                            writer.Set(SKShader.CreateColor(shared.Color));
                        });
                    bindings.Resource(
                        "tintB",
                        token,
                        ShaderResourceCoordinateSpace.OutputDevice,
                        0.5f,
                        static (writer, shared, opacity, _) =>
                        {
                            shared.Uses++;
                            shared.LastOpacity = opacity;
                            writer.Set(SKShader.CreateColor(shared.Color.WithAlpha((byte)(opacity * 255))));
                        });
                });
            RenderFragmentHandle source = context.OpaqueSource(SourceDescription(Colors.White));
            context.Publish(context.Shader(source, description));
        });

        using RenderNodeRasterization rasterization = Rasterize(node);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(color.Uses, Is.EqualTo(2), "both bindings resolve the shared resource");
            Assert.That(color.LastOpacity, Is.EqualTo(0.5f), "the value-carrying binding keeps its own value");
        }
    }

    [Test]
    public void AShaderDescriptionBindingOneHitTestSlotTwice_IsRefused()
    {
        var color = new ShaderColor(SKColors.MediumPurple);
        Exception? failure = null;
        using var node = new DelegateNode(context =>
        {
            RenderResource<ShaderColor> token = context.Borrow(color);
            RenderResourceBinding binding = s_sharedColorSlot.Bind(token);
            failure = Assert.Throws<ArgumentException>(() => ShaderDescription.WholeSource(
                WholeSource,
                RenderBoundsContract.Identity,
                bindings => bindings.Resource(
                    "tint",
                    token,
                    ShaderResourceCoordinateSpace.OutputDevice,
                    static (writer, shared, _) => writer.Set(SKShader.CreateColor(shared.Color))),
                hitTest: RenderHitTestContract.FromSlot<ShaderColor>(
                    s_sharedColorSlot,
                    static (_, _) => true),
                hitTestResources: [binding, binding],
                slots: [s_sharedColorSlot]));
            context.Publish(context.OpaqueSource(SourceDescription(Colors.White)));
        });

        Measure(node);

        Assert.That(failure, Is.Not.Null, "a declared slot is bound once, not once per binding written");
    }

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
            Assert.That(exportedTypes, Does.Contain("Beutl.Graphics.Shaders.ShaderDescription"));
            Assert.That(exportedTypes, Does.Contain("Beutl.Graphics.Shaders.ShaderBindingBuilder"));
            Assert.That(exportedTypes, Does.Not.Contain("Beutl.Graphics.Shaders.ShaderUniformBinding"));
            Assert.That(exportedTypes, Does.Not.Contain("Beutl.Graphics.Shaders.ShaderResourceBinding"));
            Assert.That(exportedTypes, Does.Not.Contain("Beutl.Graphics.Shaders.SpirvShaderLowering"));
            Assert.That(methods, Has.Length.EqualTo(1), "the description is the only route into a stage");
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


    [Test]
    public void AWholeSourceShaderThatRelocatesItsInput_HitsWhereItsDeclaredContractSays()
    {
        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(SourceDescription(Colors.White));
            context.Publish(context.Shader(source, s_relocatingDescription));
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

    [Test]
    public void AWholeSourceShaderDeclaringNoHitTest_StillForwardsTheQuestionToItsInputUnchanged()
    {
        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(SourceDescription(Colors.White));
            context.Publish(context.Shader(source, s_undeclaredRelocatingDescription));
        });

        using var renderer = CreateHitTestRenderer(node);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(renderer.HitTest(MovedPoint()), Is.False);
            Assert.That(renderer.HitTest(VacatedPoint()), Is.True);
        }
    }

    [Test]
    public void AWholeSourceHitTest_ReadsTheResourceItsOwnDescriptionBound()
    {
        var color = new ShaderColor(SKColors.MediumPurple);

        using var node = new DelegateNode(context =>
        {
            RenderResource<ShaderColor> token = context.Borrow(color);
            ShaderDescription description = ShaderDescription.WholeSource(
                WholeSource,
                RenderBoundsContract.Identity,
                bindings => bindings.Resource(
                    "tint",
                    token,
                    ShaderResourceCoordinateSpace.OutputDevice,
                    static (writer, tint, _) => writer.Set(SKShader.CreateColor(tint.Color))),
                hitTest: RenderHitTestContract.FromSlot<ShaderColor>(
                    s_colorSlot,
                    static (tint, point) =>
                    {
                        tint.HitTests++;
                        return tint.Color.Alpha > 0 && point.X < 4;
                    }),
                hitTestResources: [s_colorSlot.Bind(token)],
                slots: [s_colorSlot]);
            RenderFragmentHandle source = context.OpaqueSource(SourceDescription(Colors.White));
            context.Publish(context.Shader(source, description));
        });

        using var renderer = CreateHitTestRenderer(node);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(renderer.HitTest(new Point(1, 1)), Is.True);
            Assert.That(renderer.HitTest(new Point(6, 1)), Is.False);
            Assert.That(color.HitTests, Is.EqualTo(2), "the hit test resolved the slot this description bound");
        }
    }

    [Test]
    public void AWholeSourceDescriptionDeclaringAnUninitializedHitTest_IsRejectedWhereItIsDeclared()
    {
        Assert.That(
            () => ShaderDescription.WholeSource(
                TranslateWholeSource,
                TranslateBounds(),
                DeclareTranslateUniforms,
                hitTest: default(RenderHitTestContract)),
            Throws.ArgumentException.With.Message.Contains("uninitialized"));
    }

    private static OpaqueRenderDescription SourceDescription(Color color)
        => OpaqueRenderDescription.Create(
            color,
            static (session, current) =>
            {
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                output.Canvas.Use(canvas => canvas.Clear(current));
                session.Publish(output);
            },
            OpaqueRenderBoundsContract.Source(s_bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale);


    private static Point MovedPoint() => new Point(4, 3) + s_translation;

    private static Point VacatedPoint() => new(4, 3);

    private static RenderBoundsContract TranslateBounds()
        => RenderBoundsContract.Create(
            s_translation,
            static (offset, bounds) => bounds.Translate(offset),
            static (offset, required) => required.Translate(-offset));

    private static void DeclareTranslateUniforms(ShaderBindingBuilder bindings)
    {
        bindings.Uniform("dx", (float)s_translation.X);
        bindings.Uniform("dy", (float)s_translation.Y);
    }

    private static RenderNodeRenderer CreateHitTestRenderer(RenderNode node)
        => new(
            node,
            new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            });

    private static RenderNodeMeasurement Measure(RenderNode node)
    {
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRenderRequest { Intent = RenderIntent.Preview });
        return renderer.Measure();
    }

    private static RenderNodeRasterization Rasterize(RenderNode node)
    {
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
        }, new CpuTargetFactory());
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
