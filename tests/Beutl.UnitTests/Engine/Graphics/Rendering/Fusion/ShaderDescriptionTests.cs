using System.Buffers.Binary;
using System.Numerics;
using System.Reflection;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Fusion;

[TestFixture]
public sealed class ShaderDescriptionTests
{
    private const string IdentityCurrentPixel = "half4 apply(half4 color) { return color; }";

    [Test]
    public void CurrentPixel_NormalizesSourceAndRejectsUnsafeGrammar()
    {
        ShaderDescription first = ShaderDescription.CurrentPixel(
            "\r\nhalf4 apply(half4 color) {\r\n    return color;\r\n}\r\n");
        ShaderDescription second = ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) {\n    return color;\n}");

        Assert.Multiple(() =>
        {
            Assert.That(first.Kind, Is.EqualTo(ShaderDescriptionKind.CurrentPixel));
            Assert.That(first.Source.Text, Is.EqualTo(second.Source.Text));
            Assert.That(first.Source.IdentityHash, Is.EqualTo(second.Source.IdentityHash));
            Assert.That(first.Bounds, Is.EqualTo(RenderBoundsContract.Identity));
            Assert.That(
                typeof(ShaderDescription).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Select(static property => property.Name),
                Does.Not.Contain("IsCoverageHomogeneous"));
        });

        string[] invalidSources =
        [
            "half4 main(float2 coord) { return half4(1); }",
            "half4 apply(half4 pixel) { return pixel; }",
            "half4 apply(half4 color) { return half4(sk_FragCoord.xy, 0, 1); }",
            "uniform shader src; half4 apply(half4 color) { return src.eval(color.rg); }",
            "uniform float left, right; half4 apply(half4 color) { return color; }",
            "struct Payload { float value; }; half4 apply(half4 color) { return color; }",
            "half4 apply(half4 color) { return color; } half4 apply(half4 color) { return color; }",
        ];

        foreach (string source in invalidSources)
        {
            Assert.That(
                () => ShaderDescription.CurrentPixel(source),
                Throws.TypeOf<ArgumentException>(),
                source);
        }
    }

    [Test]
    public void CurrentPixel_SpirvProgramIdentityIncludesTheNativeSource()
    {
        const string sksl = "half4 apply(half4 color) { return color; }";
        const string glsl =
            "#version 450\nlayout(location=0) out vec4 color; void main() { color = vec4(1); }";
        var firstLowering = new SpirvShaderLowering(
            glsl,
            []);
        var equivalentLowering = new SpirvShaderLowering(
            glsl,
            []);
        var differentLowering = new SpirvShaderLowering(
            "#version 450\nlayout(location=0) out vec4 color; void main() { color = vec4(0); }",
            []);
        SkslSource source = new(sksl, ShaderDescriptionKind.CurrentPixel);
        ShaderDescription first = ShaderDescription.CurrentPixel(source, firstLowering, bindings: null);
        ShaderDescription equivalent = ShaderDescription.CurrentPixel(source, equivalentLowering, bindings: null);
        ShaderDescription different = ShaderDescription.CurrentPixel(source, differentLowering, bindings: null);

        Assert.Multiple(() =>
        {
            Assert.That(first.SpirvLowering!.ProgramIdentity,
                Is.EqualTo(equivalent.SpirvLowering!.ProgramIdentity));
            Assert.That(first.SpirvLowering.ProgramIdentity,
                Is.Not.EqualTo(different.SpirvLowering!.ProgramIdentity));
        });
    }

    [Test]
    public void LumaColor_ReusesOneSkslDescriptionWithoutNativeLowering()
    {
        ShaderDescription first = BuiltInColorFilterShader.LumaColor();
        ShaderDescription second = BuiltInColorFilterShader.LumaColor();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.SameAs(second));
            Assert.That(first.SpirvLowering, Is.Null);
        });
    }

    [Test]
    public void SpirvLowering_RejectsDuplicateAndInvalidPushConstantOffsets()
    {
        const string glsl =
            "#version 450\nlayout(location=0) out vec4 color; void main() { color = vec4(1); }";

        Assert.Multiple(() =>
        {
            Assert.That(
                () => new SpirvShaderLowering(
                    glsl,
                    [new SpirvPushConstantBinding("value", 16), new SpirvPushConstantBinding("value", 20)]),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => new SpirvShaderLowering(
                    glsl,
                    [new SpirvPushConstantBinding("value", SpirvPushConstants.UserByteOffset - 4)]),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => new SpirvShaderLowering(
                    glsl,
                    [new SpirvPushConstantBinding("value", SpirvPushConstants.UserByteOffset + 2)]),
                Throws.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public void CurrentPixel_SpirvLoweringRequiresOneMatchingMappingPerUniform()
    {
        const string source =
            "uniform float value; half4 apply(half4 color) { return color * value; }";

        Assert.Multiple(() =>
        {
            Assert.That(
                () => CreateSpirvDescription(source, [], bindings => bindings.Uniform("value", 1f)),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => CreateSpirvDescription(
                    source,
                    [new SpirvPushConstantBinding("other", SpirvPushConstants.UserByteOffset)],
                    bindings => bindings.Uniform("value", 1f)),
                Throws.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public void CurrentPixel_SpirvLoweringRejectsOverlappingOrInvalidLayouts()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                () => CreateSpirvDescription(
                    "uniform float left; uniform float right; "
                    + "half4 apply(half4 color) { return color * left * right; }",
                    [
                        new SpirvPushConstantBinding("left", SpirvPushConstants.UserByteOffset),
                        new SpirvPushConstantBinding("right", SpirvPushConstants.UserByteOffset),
                    ],
                    bindings =>
                    {
                        bindings.Uniform("left", 1f);
                        bindings.Uniform("right", 1f);
                    }),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => CreateSpirvDescription(
                    "uniform float value; half4 apply(half4 color) { return color * value; }",
                    [new SpirvPushConstantBinding("value", SpirvPushConstants.ByteSize)],
                    bindings => bindings.Uniform("value", 1f)),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => CreateSpirvDescription(
                    "uniform float2 value; half4 apply(half4 color) { return color * value.x; }",
                    [new SpirvPushConstantBinding("value", SpirvPushConstants.UserByteOffset + 4)],
                    bindings => bindings.Uniform("value", Vector2.One)),
                Throws.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public void SpirvLowering_BindsDirectUniformCanonicalValues()
    {
        ShaderDescription description = CreateSpirvDescription(
            "uniform float gain; uniform float2 offset; "
            + "half4 apply(half4 color) { return color * gain + half4(offset, 0, 0); }",
            [
                new SpirvPushConstantBinding("gain", SpirvPushConstants.UserByteOffset),
                new SpirvPushConstantBinding("offset", SpirvPushConstants.UserByteOffset + 8),
            ],
            bindings =>
            {
                bindings.Uniform("gain", 0.5f);
                bindings.Uniform("offset", new Vector2(1.25f, -2.5f));
            });

        SpirvPushConstants constants = description.SpirvLowering!.Bind(
            description,
            context: null,
            new PixelPoint(7, -3));
        ReadOnlySpan<byte> bytes = constants;
        int sourceX = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        int sourceY = BinaryPrimitives.ReadInt32LittleEndian(bytes[sizeof(int)..]);
        float gain = ReadSingle(bytes, SpirvPushConstants.UserByteOffset);
        float offsetX = ReadSingle(bytes, SpirvPushConstants.UserByteOffset + 8);
        float offsetY = ReadSingle(bytes, SpirvPushConstants.UserByteOffset + 12);

        Assert.Multiple(() =>
        {
            Assert.That(description.HasExecutionContextBinder, Is.False);
            Assert.That(sourceX, Is.EqualTo(7));
            Assert.That(sourceY, Is.EqualTo(-3));
            Assert.That(gain, Is.EqualTo(0.5f));
            Assert.That(offsetX, Is.EqualTo(1.25f));
            Assert.That(offsetY, Is.EqualTo(-2.5f));
        });
    }

    [Test]
    public void SpirvLowering_CustomUniformBinderUsesAndInvalidatesExecutionContext()
    {
        ShaderUniformWriter? retainedWriter = null;
        ShaderExecutionContext? retainedContext = null;
        ShaderDescription description = CreateSpirvDescription(
            "uniform float gain; half4 apply(half4 color) { return color * gain; }",
            [new SpirvPushConstantBinding("gain", SpirvPushConstants.UserByteOffset)],
            bindings => bindings.Uniform(
                "gain",
                0.5f,
                (writer, value, execution) =>
                {
                    retainedWriter = writer;
                    retainedContext = execution;
                    writer.Set(value * execution.OutputScale);
                }));
        var token = new RenderExecutionSessionToken();
        var context = new ShaderExecutionContext(
            token,
            new Rect(0, 0, 10, 10),
            new Rect(0, 0, 10, 10),
            new Rect(0, 0, 10, 10),
            new PixelRect(0, 0, 10, 10),
            default,
            EffectiveScale.At(1),
            outputScale: 3,
            workingScale: 1,
            maxWorkingScale: 3,
            intent: RenderIntent.Preview,
            purpose: RenderRequestPurpose.Frame);

        SpirvPushConstants constants = token.RunAndComplete(
            () => description.SpirvLowering!.Bind(
                description,
                context,
                new PixelPoint(2, 4)));
        ReadOnlySpan<byte> bytes = constants;
        float gain = ReadSingle(bytes, SpirvPushConstants.UserByteOffset);

        Assert.Multiple(() =>
        {
            Assert.That(description.HasExecutionContextBinder, Is.True);
            Assert.That(gain, Is.EqualTo(1.5f));
            Assert.That(retainedWriter, Is.Not.Null);
            Assert.That(retainedContext, Is.SameAs(context));
            Assert.That(() => retainedWriter!.Set(1f), Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => _ = retainedContext!.OutputBounds, Throws.TypeOf<InvalidOperationException>());
        });
    }

    private static float ReadSingle(ReadOnlySpan<byte> bytes, int offset)
        => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes[offset..]));

    private static ShaderDescription CreateSpirvDescription(
        string source,
        IReadOnlyList<SpirvPushConstantBinding> mappings,
        Action<ShaderBindingBuilder> bindings)
        => ShaderDescription.CurrentPixel(
            new SkslSource(source, ShaderDescriptionKind.CurrentPixel),
            new SpirvShaderLowering(
                "#version 450\nlayout(location=0) out vec4 color; void main() { color = vec4(1); }",
                mappings),
            bindings);

    [Test]
    public void CurrentPixel_AcceptsOnlyRenameSafeValueDerivedGrammar()
    {
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<object> resource = registry.RegisterBorrowed(new object());
        ShaderDescription description = ShaderDescription.CurrentPixel(
            """
            uniform float gain;
            uniform float2 offset;
            uniform shader lut;
            const float bias = 0.125;
            const float weights[2] = float[2](0.25, 0.75);

            half3 adjust(half3 value, float amount)
            {
                half3 adjusted = clamp(value * amount + bias, 0.0, 1.0);
                return adjusted;
            }

            half4 apply(half4 color)
            {
                float2 lookup = color.rg + offset;
                half3 rgb = adjust(color.rgb, gain) * weights[0] + color.rgb * weights[1];
                return half4(lut.eval(lookup).rgb * rgb, color.a);
            }
            """,
            bindings =>
            {
                bindings.Uniform("gain", 0.5f);
                bindings.Uniform("offset", Vector2.Zero);
                bindings.Resource(
                    "lut",
                    resource,
                    ShaderResourceCoordinateSpace.Value,
                    static (writer, _, _) => writer.Set(SKShader.CreateColor(StableColors.White)));
            });

        Assert.Multiple(() =>
        {
            Assert.That(description.Kind, Is.EqualTo(ShaderDescriptionKind.CurrentPixel));
            Assert.That(description.Uniforms, Has.Count.EqualTo(2));
            Assert.That(description.Resources, Has.Count.EqualTo(1));
        });
    }

    [TestCase("float leaked; half4 apply(half4 color) { return color; }")]
    [TestCase("layout(color) uniform half4 tint; half4 apply(half4 color) { return color; }")]
    [TestCase("#define GAIN 2\nhalf4 apply(half4 color) { return color * GAIN; }")]
    [TestCase("half4 helper(half4 value); half4 apply(half4 color) { return helper(color); }")]
    [TestCase("half4 helper(inout half4 value) { return value; } half4 apply(half4 color) { return helper(color); }")]
    [TestCase("half4 apply(half4 color) { float left = 1, right = 2; return color * left; }")]
    [TestCase("half4 apply(half4 color) { for (int x = 0, y = 0; x < 1; ++x) { } return color; }")]
    [TestCase("half4 apply(half4 color) { float color = 1; return half4(color); }")]
    [TestCase("half4 apply(half4 color) { return half4(dFdx(color.r)); }")]
    [TestCase("uniform shader lut; half4 apply(half4 color) { return lut.eval(sk_FragCoord.xy); }")]
    [TestCase("uniform shader lut; half4 apply(half4 color) { return lut.eval(unknownValue); }")]
    [TestCase("uniform shader lut; half4 apply(half4 color) { float2 position; return lut.eval(position); }")]
    [TestCase("uniform shader lut; half4 apply(half4 color) { return lut.eval(); }")]
    [TestCase("uniform shader lut; half4 apply(half4 color) { return lut.eval(color.rg, color.ba); }")]
    [TestCase("uniform shader lut; const half4 sampled = lut.eval(); half4 apply(half4 color) { return sampled; }")]
    [TestCase("uniform shader lut; half4 apply(half4 color) { return half4(lut); }")]
    [TestCase("uniform float __beutl_value; half4 apply(half4 color) { return color; }")]
    public void CurrentPixel_RejectsGrammarThatCannotBeProvenValueOnly(string source)
    {
        Assert.That(
            () => new SkslSource(source, ShaderDescriptionKind.CurrentPixel),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void WholeSource_RemainsACompleteCoordinateShader()
    {
        RenderBoundsContract bounds = RenderBoundsContract.Create(
            static input => input,
            static requested => requested);

        ShaderDescription description = ShaderDescription.WholeSource(
            """
            uniform shader src;
            half4 sampleSource(float2 position) { return src.eval(position); }
            half4 main(float2 coord)
            {
                float2 first = coord, second = coord + float2(1);
                return sampleSource(mix(first, second, 0.5));
            }
            """,
            bounds);

        Assert.That(description.Kind, Is.EqualTo(ShaderDescriptionKind.WholeSource));
    }

    [TestCase("const float __beutl_pixel = 1.0;")]
    [TestCase("half4 __beutl_s7_sample(float2 coord) { return src.eval(coord); }")]
    public void WholeSource_RejectsRendererReservedTopLevelDeclarations(string declaration)
    {
        Assert.That(
            () => ShaderDescription.WholeSource(
                $"uniform shader src; {declaration} half4 main(float2 coord) {{ return src.eval(coord); }}",
                RenderBoundsContract.Identity),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void WholeSource_AllowsNonGeneratedRendererPrefixOnTopLevelNames()
    {
        ShaderDescription description = ShaderDescription.WholeSource(
            "uniform shader src; const float __beutl_custom = 1.0; "
            + "half4 main(float2 coord) { return src.eval(coord) * __beutl_custom; }",
            RenderBoundsContract.Identity);

        Assert.That(description.Kind, Is.EqualTo(ShaderDescriptionKind.WholeSource));
    }

    [Test]
    public void WholeSource_RejectsCommaSeparatedRendererGeneratedDeclaration()
    {
        Assert.That(
            () => ShaderDescription.WholeSource(
                """
                uniform shader src;
                half __beutl_head_main, keep;
                half4 main(float2 coord) { return src.eval(coord); }
                """,
                RenderBoundsContract.Identity),
            Throws.TypeOf<ArgumentException>()
                .With.Message.Contains("__beutl_head_main"));
    }

    [Test]
    public void WholeSource_AllowsRendererPrefixOnFunctionLocalNames()
    {
        ShaderDescription description = ShaderDescription.WholeSource(
            "uniform shader src; half4 main(float2 coord) { "
            + "float2 __beutl_pixel = coord; return src.eval(__beutl_pixel); }",
            RenderBoundsContract.Identity);

        Assert.That(description.Kind, Is.EqualTo(ShaderDescriptionKind.WholeSource));
    }

    [Test]
    public void WholeSource_RequiresImplicitSourceAndExplicitBounds()
    {
        RenderBoundsContract bounds = RenderBoundsContract.Create(
            static input => input.Inflate(new Thickness(2)),
            static requested => requested.Inflate(new Thickness(2)));
        ShaderDescription description = ShaderDescription.WholeSource(
            "uniform shader src; half4 main(float2 coord) { return src.eval(coord); }",
            bounds,
            sourceTileMode: SKShaderTileMode.Clamp);

        Assert.Multiple(() =>
        {
            Assert.That(description.Kind, Is.EqualTo(ShaderDescriptionKind.WholeSource));
            Assert.That(description.Bounds, Is.EqualTo(bounds));
            Assert.That(description.SourceTileMode, Is.EqualTo(SKShaderTileMode.Clamp));
            Assert.That(
                () => ShaderDescription.WholeSource(
                    "half4 main(float2 coord) { return half4(1); }",
                    bounds),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => ShaderDescription.WholeSource(
                    "uniform shader src; half4 main(float2 coord) { return src.eval(coord); }",
                    default),
                Throws.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public void WholeSource_RejectsAnExplicitBindingForItsImplicitSource()
    {
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<object> resource = registry.RegisterBorrowed(new object());

        ArgumentException? exception = Assert.Throws<ArgumentException>(
            () => ShaderDescription.WholeSource(
                "uniform shader src; half4 main(float2 coord) { return src.eval(coord); }",
                RenderBoundsContract.Identity,
                bindings => bindings.Resource(
                    "src",
                    resource,
                    ShaderResourceCoordinateSpace.Value,
                    static (writer, _, _) => writer.Set(SKShader.CreateColor(StableColors.White)))));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.ParamName, Is.EqualTo("resources"));
            Assert.That(exception.Message, Does.Contain("implicit WholeSource input 'src'"));
        });
    }

    [Test]
    public void DirectUniforms_AreCanonicalAndValidatedAgainstDeclarations()
    {
        ShaderDescription description = ShaderDescription.CurrentPixel(
            "uniform float amount; uniform float2 offset; uniform float4 tint; "
            + "half4 apply(half4 color) { return color * amount + half4(offset, 0, 0) + tint; }",
            bindings =>
            {
                bindings.Uniform("amount", 0.5f);
                bindings.Uniform("offset", new Vector2(1, 2));
                bindings.Uniform("tint", new float[] { 0, 0, 0, 0 });
            });

        Assert.That(description.Uniforms, Has.Count.EqualTo(3));
        Assert.That(
            () => ShaderDescription.CurrentPixel(
                "uniform float2 value; half4 apply(half4 color) { return color; }",
                bindings => bindings.Uniform("value", 1f)),
            Throws.TypeOf<ArgumentException>()
                .Or.TypeOf<InvalidOperationException>());
        Assert.That(
            () => ShaderDescription.CurrentPixel(
                "uniform float value; half4 apply(half4 color) { return color; }",
                bindings => bindings.Uniform("value", 1L)),
            Throws.TypeOf<ArgumentException>());
        Assert.That(
            () => ShaderDescription.CurrentPixel(
                "uniform float value; half4 apply(half4 color) { return color; }",
                bindings =>
                {
                    bindings.Uniform("value", 1f);
                    bindings.Uniform("value", 2f);
                }),
            Throws.TypeOf<ArgumentException>());
        Assert.That(
            () => ShaderDescription.CurrentPixel(
                "uniform float value; half4 apply(half4 color) { return color; }",
                bindings => bindings.Uniform("not-valid!", 1f)),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void DirectUniforms_AreCopiedOnceAndReusedWithoutExecutionAllocations()
    {
        float[] supplied = [1, 2, 3, 4];
        ShaderDescription description = ShaderDescription.CurrentPixel(
            "uniform float gain; uniform float4 tint; half4 apply(half4 color) { return color * gain * tint; }",
            bindings =>
            {
                bindings.Uniform("gain", 0.5f);
                bindings.Uniform("tint", supplied);
            });
        ShaderUniformBinding scalarBinding = description.Uniforms.Single(binding => binding.Name == "gain");
        ShaderUniformBinding spanBinding = description.Uniforms.Single(binding => binding.Name == "tint");
        SkslUniformDeclaration scalarDeclaration = description.Source.Uniforms[scalarBinding.Name];
        SkslUniformDeclaration spanDeclaration = description.Source.Uniforms[spanBinding.Name];
        supplied[0] = 99;

        ShaderUniformValue firstScalar = scalarBinding.Bind(scalarDeclaration, context: null);
        ShaderUniformValue firstSpan = spanBinding.Bind(spanDeclaration, context: null);
        for (int index = 0; index < 100; index++)
        {
            _ = scalarBinding.Bind(scalarDeclaration, context: null);
            _ = spanBinding.Bind(spanDeclaration, context: null);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        float sum = 0;
        ShaderUniformValue currentScalar = default;
        ShaderUniformValue currentSpan = default;
        for (int index = 0; index < 20_000; index++)
        {
            currentScalar = scalarBinding.Bind(scalarDeclaration, context: null);
            currentSpan = spanBinding.Bind(spanDeclaration, context: null);
            sum += currentScalar.Floats![0] + currentSpan.Floats![0];
        }
        long bytesPerExecution = (GC.GetAllocatedBytesForCurrentThread() - before) / 20_000;

        Assert.Multiple(() =>
        {
            Assert.That(firstSpan.Floats, Is.EqualTo(new[] { 1f, 2f, 3f, 4f }));
            Assert.That(currentScalar.Floats, Is.SameAs(firstScalar.Floats));
            Assert.That(currentSpan.Floats, Is.SameAs(firstSpan.Floats));
            Assert.That(sum, Is.EqualTo(30_000));
            Assert.That(bytesPerExecution, Is.Zero);
        });
    }

    [Test]
    public void DirectUniform_UInt32AboveInt32MaxValueReportsRangeError()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => ShaderDescription.CurrentPixel(
                "uniform int value; half4 apply(half4 color) { return color; }",
                bindings => bindings.Uniform("value", uint.MaxValue)))!;

        Assert.Multiple(() =>
        {
            Assert.That(exception.ParamName, Is.EqualTo("value"));
            Assert.That(exception.ActualValue, Is.EqualTo(uint.MaxValue));
            Assert.That(exception.Message, Does.Contain("Int32.MaxValue"));
        });
    }

    [Test]
    public void ResourceBindings_EnforceCoordinateSpaceAndDeclaredType()
    {
        using var registry = new RenderRequestResourceRegistry();
        var resource = new object();
        RenderResource<object> token = registry.RegisterBorrowed(resource);

        ShaderDescription current = ShaderDescription.CurrentPixel(
            "uniform shader lut; half4 apply(half4 color) { return lut.eval(color.rg); }",
            bindings => bindings.Resource(
                "lut",
                token,
                ShaderResourceCoordinateSpace.Value,
                static (writer, _, _) => writer.Set(SKShader.CreateColor(StableColors.White))));
        Assert.That(current.Resources.Single().CoordinateSpace, Is.EqualTo(ShaderResourceCoordinateSpace.Value));

        Assert.That(
            () => ShaderDescription.CurrentPixel(
                "uniform shader lut; half4 apply(half4 color) { return lut.eval(color.rg); }",
                bindings => bindings.Resource(
                    "lut",
                    token,
                    ShaderResourceCoordinateSpace.OutputDevice,
                    static (writer, _, _) => writer.Set(SKShader.CreateColor(StableColors.White)))),
            Throws.TypeOf<ArgumentException>());
        Assert.That(
            () => ShaderDescription.CurrentPixel(
                "uniform float value; half4 apply(half4 color) { return color * value; }",
                bindings => bindings.Resource(
                    "value",
                    token,
                    ShaderResourceCoordinateSpace.Value,
                    static (writer, _, _) => writer.Set(SKShader.CreateColor(StableColors.White)))),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ScopedCustomBinder_CannotRetainWriterAndDefaultCachePolicyIsRequestUnique()
    {
        ShaderUniformWriter? retained = null;
        ShaderDescription description = ShaderDescription.CurrentPixel(
            "uniform float amount; half4 apply(half4 color) { return color * amount; }",
            bindings => bindings.Uniform(
                "amount",
                0.25f,
                (writer, value, _) =>
                {
                    retained = writer;
                    writer.Set(value);
                }));
        var token = new RenderExecutionSessionToken();
        var execution = new ShaderExecutionContext(
            token,
            new Rect(0, 0, 10, 10),
            new Rect(0, 0, 10, 10),
            new Rect(0, 0, 10, 10),
            new PixelRect(0, 0, 10, 10),
            default,
            EffectiveScale.At(1),
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 2,
            intent: RenderIntent.Preview,
            purpose: RenderRequestPurpose.Frame);

        _ = description.Uniforms.Single().Bind(
            new SkslUniformDeclaration("float", null),
            execution);
        token.Complete();

        Assert.Multiple(() =>
        {
            Assert.That(() => retained!.Set(0.5f), Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => _ = execution.OutputBounds, Throws.TypeOf<InvalidOperationException>());
        });
    }

    // SkSL reads matrix uniform data column-major, so a canonical matrix value must be the column-major encoding
    // of the SkSL matrix that reproduces the source type's own transform convention.
    [Test]
    public void CanonicalMatrixValues_AreColumnMajorForTheEquivalentSkslMatrix()
    {
        // SKMatrix transforms column vectors (p' = M * p) and stores its rows contiguously, so the canonical
        // value is its storage order transposed. A translation must therefore land in the last column.
        var skMatrix = SKMatrix.CreateTranslation(50, 70);
        float[] skValues = ShaderUniformValue.Create(skMatrix).Floats!;

        // Matrix4x4 transforms row vectors (p' = p * M) and stores its rows contiguously. The equivalent
        // column-vector matrix is its transpose, whose column-major encoding is that same storage order.
        Matrix4x4 numericsMatrix = Matrix4x4.CreateTranslation(50, 70, 90);
        float[] numericsValues = ShaderUniformValue.Create(numericsMatrix).Floats!;

        // Matrix3x2 has no SkSL matrix type. Its six floats bind to float2[3]: x basis, y basis, translation.
        var affine = Matrix3x2.CreateScale(2, 3) * Matrix3x2.CreateTranslation(50, 70);
        float[] affineValues = ShaderUniformValue.Create(affine).Floats!;

        Assert.Multiple(() =>
        {
            Assert.That(skValues, Is.EqualTo(new float[]
            {
                1, 0, 0,
                0, 1, 0,
                50, 70, 1,
            }));
            Assert.That(numericsValues, Is.EqualTo(new float[]
            {
                1, 0, 0, 0,
                0, 1, 0, 0,
                0, 0, 1, 0,
                50, 70, 90, 1,
            }));
            Assert.That(affineValues, Is.EqualTo(new float[]
            {
                2, 0,
                0, 3,
                50, 70,
            }));
        });
    }

    // The two conventions agree once both are expressed as an SkSL matrix, so a transform built either way must
    // produce the same canonical value. This is what makes the differing member order above correct.
    [Test]
    public void CanonicalMatrixValues_AgreeBetweenSkiaAndNumericsForTheSameTransform()
    {
        var skMatrix = SKMatrix.CreateScaleTranslation(2, 3, 50, 70);
        Matrix4x4 numericsMatrix = Matrix4x4.CreateScale(2, 3, 1) * Matrix4x4.CreateTranslation(50, 70, 0);

        float[] skValues = ShaderUniformValue.Create(skMatrix).Floats!;
        float[] numericsValues = ShaderUniformValue.Create(numericsMatrix).Floats!;

        // The 3x3 columns are the 4x4 columns with the z row and column dropped.
        float[] projected =
        [
            numericsValues[0], numericsValues[1], numericsValues[3],
            numericsValues[4], numericsValues[5], numericsValues[7],
            numericsValues[12], numericsValues[13], numericsValues[15],
        ];

        Assert.That(skValues, Is.EqualTo(projected));
    }

    [Test]
    public void CanonicalMatrixValues_BindToTheDeclaredSkslMatrixType()
    {
        ShaderDescription description = ShaderDescription.CurrentPixel(
            "uniform float3x3 xform; uniform float2 basis[3]; "
            + "half4 apply(half4 color) { return color * (xform[0][0] + basis[2].x); }",
            bindings =>
            {
                bindings.Uniform("xform", SKMatrix.CreateTranslation(50, 70));
                bindings.Uniform("basis", Matrix3x2.Identity);
            });

        Assert.Multiple(() =>
        {
            Assert.That(description.Uniforms, Has.Count.EqualTo(2));

            // float3x3 takes nine floats; a Matrix4x4 supplies sixteen and must be rejected.
            Assert.That(
                () => ShaderDescription.CurrentPixel(
                    "uniform float3x3 xform; half4 apply(half4 color) { return color * xform[0][0]; }",
                    bindings => bindings.Uniform("xform", Matrix4x4.Identity)),
                Throws.TypeOf<InvalidOperationException>().Or.TypeOf<ArgumentException>());
        });
    }

    [Test]
    public void BuilderRetainedPastConstruction_RejectsFurtherDeclarations()
    {
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<object> resource = registry.RegisterBorrowed(new object());
        ShaderBindingBuilder? retained = null;
        ShaderDescription description = ShaderDescription.CurrentPixel(
            "uniform float gain; half4 apply(half4 color) { return color * gain; }",
            bindings =>
            {
                retained = bindings;
                bindings.Uniform("gain", 0.5f);
            });

        Assert.That(retained, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(
                () => retained!.Uniform("late", 1f),
                Throws.InvalidOperationException.With.Message.Contains("late")
                    .And.Message.Contains("already been created"));
            Assert.That(
                () => retained!.Uniform("lateSpan", new float[] { 1f }),
                Throws.InvalidOperationException);
            Assert.That(
                () => retained!.Uniform("lateBound", 1f, static (writer, value, _) => writer.Set(value)),
                Throws.InvalidOperationException);
            Assert.That(
                () => retained!.Resource(
                    "lateResource",
                    resource,
                    ShaderResourceCoordinateSpace.Value,
                    static (writer, _, _) => writer.Set(SKShader.CreateColor(new SKColor(0xFFFFFFFF)))),
                Throws.InvalidOperationException);
            Assert.That(
                () => retained!.Resource(
                    "lateValuedResource",
                    resource,
                    ShaderResourceCoordinateSpace.Value,
                    0f,
                    static (writer, _, _, _) => writer.Set(SKShader.CreateColor(new SKColor(0xFFFFFFFF)))),
                Throws.InvalidOperationException);
            Assert.That(description.Uniforms, Has.Count.EqualTo(1));
            Assert.That(description.Resources, Is.Empty);
        });
    }

    [Test]
    public void BindingsDeclaredWhileTheCallbackRuns_ReachTheDescription()
    {
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<object> resource = registry.RegisterBorrowed(new object());
        ShaderDescription description = ShaderDescription.CurrentPixel(
            "uniform float gain; uniform float2 offset; uniform shader lut; "
            + "half4 apply(half4 color) { return lut.eval(color.rg + offset) * gain; }",
            bindings =>
            {
                bindings.Uniform("gain", 0.5f);
                bindings.Uniform("offset", Vector2.One);
                bindings.Resource(
                    "lut",
                    resource,
                    ShaderResourceCoordinateSpace.Value,
                    static (writer, _, _) => writer.Set(SKShader.CreateColor(new SKColor(0xFFFFFFFF))));
            });

        Assert.Multiple(() =>
        {
            Assert.That(
                description.Uniforms.Select(static uniform => uniform.Name),
                Is.EqualTo(new[] { "gain", "offset" }));
            Assert.That(
                description.Resources.Select(static item => item.Name),
                Is.EqualTo(new[] { "lut" }));
        });
    }

    [Test]
    public void DeclarationFailingAfterItsNameIsAccepted_LeavesThatNameUnsupplied()
    {
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<object> resource = registry.RegisterBorrowed(new object());

        Assert.Multiple(() =>
        {
            Assert.That(
                () => ShaderDescription.CurrentPixel(
                    "uniform float gain; half4 apply(half4 color) { return color * gain; }",
                    static bindings =>
                    {
                        try
                        {
                            bindings.Uniform("gain", ReadOnlySpan<float>.Empty);
                        }
                        catch (ArgumentException)
                        {
                        }
                    }),
                Throws.ArgumentException.With.Message.Contains("'gain' was declared but not supplied"));
            Assert.That(
                () => ShaderDescription.CurrentPixel(
                    "uniform shader lut; half4 apply(half4 color) { return lut.eval(color.rg); }",
                    bindings =>
                    {
                        try
                        {
                            bindings.Resource(
                                "lut",
                                resource,
                                (ShaderResourceCoordinateSpace)(-1),
                                static (writer, _, _) => writer.Set(SKShader.CreateColor(new SKColor(0xFFFFFFFF))));
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                        }
                    }),
                Throws.ArgumentException.With.Message.Contains("'lut' was declared but not supplied"));
        });
    }

    private sealed record CollisionKey(string Value)
    {
        public override int GetHashCode() => 7;
    }
}
