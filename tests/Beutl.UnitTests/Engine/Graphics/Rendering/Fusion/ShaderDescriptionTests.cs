using System.Numerics;
using System.Reflection;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
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
    public void CurrentPixel_AcceptsOnlyRenameSafeValueDerivedGrammar()
    {
        using var registry = new RenderRequestResourceRegistry();
        RenderResource<object> resource = registry.RegisterBorrowed(new object(), "lut", 1);
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
                    static (writer, _, _) => writer.Set(SKShader.CreateColor(SKColors.White)));
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
            static requested => requested,
            "whole-source-coordinate-test");

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

    [Test]
    public void WholeSource_RequiresImplicitSourceAndExplicitBounds()
    {
        RenderBoundsContract bounds = RenderBoundsContract.Create(
            static input => input.Inflate(new Thickness(2)),
            static requested => requested.Inflate(new Thickness(2)),
            "whole-source-bounds");
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
    public void ResourceBindings_EnforceCoordinateSpaceAndDeclaredType()
    {
        using var registry = new RenderRequestResourceRegistry();
        var resource = new object();
        RenderResource<object> token = registry.RegisterBorrowed(resource, "resource", 1);

        ShaderDescription current = ShaderDescription.CurrentPixel(
            "uniform shader lut; half4 apply(half4 color) { return lut.eval(color.rg); }",
            bindings => bindings.Resource(
                "lut",
                token,
                ShaderResourceCoordinateSpace.Value,
                static (writer, _, _) => writer.Set(SKShader.CreateColor(SKColors.White)),
                structuralKey: "lut"));
        Assert.That(current.Resources.Single().CoordinateSpace, Is.EqualTo(ShaderResourceCoordinateSpace.Value));

        Assert.That(
            () => ShaderDescription.CurrentPixel(
                "uniform shader lut; half4 apply(half4 color) { return lut.eval(color.rg); }",
                bindings => bindings.Resource(
                    "lut",
                    token,
                    ShaderResourceCoordinateSpace.OutputDevice,
                    static (writer, _, _) => writer.Set(SKShader.CreateColor(SKColors.White)))),
            Throws.TypeOf<ArgumentException>());
        Assert.That(
            () => ShaderDescription.CurrentPixel(
                "uniform float value; half4 apply(half4 color) { return color * value; }",
                bindings => bindings.Resource(
                    "value",
                    token,
                    ShaderResourceCoordinateSpace.Value,
                    static (writer, _, _) => writer.Set(SKShader.CreateColor(SKColors.White)))),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ScopedCustomBinder_CannotRetainWriterAndNullRuntimeIdentityIsRequestUnique()
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
                },
                structuralKey: "custom-amount"));
        var token = new RenderExecutionSessionToken();
        var execution = new ShaderExecutionContext(
            token,
            new Rect(0, 0, 10, 10),
            new Rect(0, 0, 10, 10),
            new Rect(0, 0, 10, 10),
            new PixelRect(0, 0, 10, 10),
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
            Assert.That(description.Uniforms.Single().RuntimeIdentity, Is.Null);
        });
    }

    [Test]
    public void StructuralIdentity_UsesFullEqualityAfterHashCollision()
    {
        ShaderDescription first = ShaderDescription.CurrentPixel(
            "uniform float amount; " + IdentityCurrentPixel,
            bindings => bindings.Uniform(
                "amount",
                1f,
                static (writer, value, _) => writer.Set(value),
                new CollisionKey("first"),
                new RenderRuntimeIdentity("runtime")));
        ShaderDescription second = ShaderDescription.CurrentPixel(
            "uniform float amount; " + IdentityCurrentPixel,
            bindings => bindings.Uniform(
                "amount",
                1f,
                static (writer, value, _) => writer.Set(value),
                new CollisionKey("second"),
                new RenderRuntimeIdentity("runtime")));

        Assert.Multiple(() =>
        {
            Assert.That(first.StructuralIdentity.GetHashCode(), Is.EqualTo(second.StructuralIdentity.GetHashCode()));
            Assert.That(first.StructuralIdentity, Is.Not.EqualTo(second.StructuralIdentity));
        });
    }

    [Test]
    public void CustomUniformRuntimeIdentity_ContainsCanonicalValueAndAdditionalIdentity()
    {
        static ShaderDescription Create(float value, object identity)
            => ShaderDescription.CurrentPixel(
                "uniform float amount; " + IdentityCurrentPixel,
                bindings => bindings.Uniform(
                    "amount",
                    value,
                    static (writer, item, _) => writer.Set(item),
                    structuralKey: "custom-amount",
                    runtimeIdentity: new RenderRuntimeIdentity(identity)));

        object first = Create(1f, "same").CreateRuntimeIdentity();
        object equal = Create(1f, "same").CreateRuntimeIdentity();
        object changedValue = Create(2f, "same").CreateRuntimeIdentity();
        object changedAdditionalIdentity = Create(1f, "different").CreateRuntimeIdentity();

        Assert.Multiple(() =>
        {
            Assert.That(equal, Is.EqualTo(first));
            Assert.That(changedValue, Is.Not.EqualTo(first));
            Assert.That(changedAdditionalIdentity, Is.Not.EqualTo(first));
        });
    }

    private sealed record CollisionKey(string Value)
    {
        public override int GetHashCode() => 7;
    }
}
