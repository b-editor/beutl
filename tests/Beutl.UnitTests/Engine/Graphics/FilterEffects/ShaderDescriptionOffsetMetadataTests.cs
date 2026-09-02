using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shaders;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class ShaderDescriptionOffsetMetadataTests
{
    private const string TranslateSource =
        "uniform shader src; uniform float dx; uniform float dy; "
        + "half4 main(float2 coord) { return src.eval(coord - float2(dx, dy)); }";

    private static readonly Rect s_bounds = new(0, 0, 8, 6);
    private static readonly Point s_content = new(4, 3);
    private static readonly Vector s_declared = new(20, 10);
    private static readonly Vector s_other = new(-30, 40);

    // Read once here rather than inside the callback: Colors.White is a get-only property whose getter
    // this compilation cannot see, so a callback naming it is not shown to answer the same way twice.
    private static readonly Color s_fill = Colors.White;

    private static readonly SkslSource s_source = SkslSource.WholeSource(TranslateSource);

    [Test]
    public void ADescriptionRebuiltPerOffset_MovesItsMetadataAndStillKeysToOnePlan()
    {
        ShaderDescription declared = CreateDescription(s_declared);
        ShaderDescription other = CreateDescription(s_other);
        using var declaredNode = new TranslateNode(declared);
        using var otherNode = new TranslateNode(other);
        using RenderNodeRenderer declaredRenderer = CreateRenderer(declaredNode);
        using RenderNodeRenderer otherRenderer = CreateRenderer(otherNode);

        Rect declaredBounds = declaredRenderer.Measure().OutputBounds;
        Rect otherBounds = otherRenderer.Measure().OutputBounds;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                BoundOffset(declared),
                Is.EqualTo(s_declared),
                "the uniforms carry the offset the description was built around");
            Assert.That(BoundOffset(other), Is.EqualTo(s_other));

            Assert.That(declaredBounds, Is.EqualTo(s_bounds.Translate(s_declared)));
            Assert.That(otherBounds, Is.EqualTo(s_bounds.Translate(s_other)));

            Assert.That(declaredRenderer.HitTest(s_content + s_declared), Is.True);
            Assert.That(otherRenderer.HitTest(s_content + s_other), Is.True);
            Assert.That(otherRenderer.HitTest(s_content + s_declared), Is.False);

            Assert.That(
                other.StructuralIdentity,
                Is.EqualTo(declared.StructuralIdentity),
                "a contract is keyed by the callback it holds, so rebuilding around one parsed source "
                + "costs the author no second plan");
        }
    }

    private static ShaderDescription CreateDescription(Vector offset)
        => ShaderDescription.WholeSource(
            s_source,
            RenderBoundsContract.Create(
                offset,
                static (value, bounds) => bounds.Translate(value),
                static (value, required) => required.Translate(-value)),
            bindings =>
            {
                bindings.Uniform("dx", (float)offset.X);
                bindings.Uniform("dy", (float)offset.Y);
            },
            SKShaderTileMode.Decal,
            hitTest: RenderHitTestContract.Custom(
                offset,
                static (value, context, point) => context.Inputs[0].HitTest(point - value)));

    private static Vector BoundOffset(ShaderDescription description)
    {
        var token = new RenderExecutionSessionToken();
        var execution = new ShaderExecutionContext(
            token,
            s_bounds,
            s_bounds,
            s_bounds,
            new PixelRect(0, 0, 8, 6),
            EffectiveScale.At(1),
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 2,
            intent: RenderIntent.Preview,
            purpose: RenderRequestPurpose.Frame);

        var offset = new Vector(
            ReadFloatUniform(description, "dx", execution),
            ReadFloatUniform(description, "dy", execution));
        token.Complete();
        return offset;
    }

    private static float ReadFloatUniform(
        ShaderDescription description,
        string name,
        ShaderExecutionContext execution)
    {
        ShaderUniformBinding binding = description.Uniforms.Single(item => item.Name == name);
        return binding.Bind(description.Source.Uniforms[name], execution).Floats![0];
    }

    private static OpaqueRenderDescription SourceDescription()
        => OpaqueRenderDescription.Create(
            s_fill,
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

    private static RenderNodeRenderer CreateRenderer(RenderNode node)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Preview,
                    CacheOptions = RenderCacheOptions.Disabled,
                },
            });

    private sealed class TranslateNode(ShaderDescription description) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(SourceDescription());
            context.Publish(context.Shader(source, description));
        }
    }
}
