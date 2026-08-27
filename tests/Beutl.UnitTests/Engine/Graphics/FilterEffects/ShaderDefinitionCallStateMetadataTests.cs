using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

/// <summary>
/// Pins which state a whole-source shader stage's bounds and hit test answer for when one definition is
/// called more than once with different spatial values.
/// </summary>
/// <remarks>
/// A definition's metadata is the shape half of the split, so it answers for the state the contract was
/// built with, and a second call that moves pixels does not move it. That is not a hole in the split: the
/// contracts take the state as an argument, and a plan is keyed by which callback a contract holds rather
/// than by the values behind it, so an author whose spatial value varies rebuilds the definition around a
/// shared parsed source and lands on the same compiled plan.
/// </remarks>
[TestFixture]
public sealed class ShaderDefinitionCallStateMetadataTests
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

    private static readonly ShaderDefinition<Vector> s_sharedDefinition = CreateDefinition(s_declared);

    [Test]
    public void OneDefinitionCalledWithASecondOffset_AnswersForTheOffsetItWasDeclaredWith()
    {
        using var declaredCall = new TranslateNode(s_sharedDefinition, s_declared);
        using var otherCall = new TranslateNode(s_sharedDefinition, s_other);
        using RenderNodeRenderer declaredRenderer = CreateRenderer(declaredCall);
        using RenderNodeRenderer otherRenderer = CreateRenderer(otherCall);

        Rect declaredBounds = declaredRenderer.Measure().OutputBounds;
        Rect otherBounds = otherRenderer.Measure().OutputBounds;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                BoundOffset(s_sharedDefinition.Call(s_declared)),
                Is.EqualTo(s_declared),
                "the uniforms are the one binding that reads the call's own state");
            Assert.That(BoundOffset(s_sharedDefinition.Call(s_other)), Is.EqualTo(s_other));

            Assert.That(declaredBounds, Is.EqualTo(s_bounds.Translate(s_declared)));
            Assert.That(
                otherBounds,
                Is.EqualTo(s_bounds.Translate(s_declared)),
                "the second call moves its pixels but not the bounds its definition declared");

            Assert.That(declaredRenderer.HitTest(s_content + s_declared), Is.True);
            Assert.That(
                otherRenderer.HitTest(s_content + s_other),
                Is.False,
                "the declared hit test inverts the declared offset, not the one this call supplied");
            Assert.That(otherRenderer.HitTest(s_content + s_declared), Is.True);
        }
    }

    [Test]
    public void ADefinitionRebuiltPerOffset_MovesItsMetadataAndStillKeysToOnePlan()
    {
        ShaderDefinition<Vector> declared = CreateDefinition(s_declared);
        ShaderDefinition<Vector> other = CreateDefinition(s_other);
        using var declaredCall = new TranslateNode(declared, s_declared);
        using var otherCall = new TranslateNode(other, s_other);
        using RenderNodeRenderer declaredRenderer = CreateRenderer(declaredCall);
        using RenderNodeRenderer otherRenderer = CreateRenderer(otherCall);

        Rect declaredBounds = declaredRenderer.Measure().OutputBounds;
        Rect otherBounds = otherRenderer.Measure().OutputBounds;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(declaredBounds, Is.EqualTo(s_bounds.Translate(s_declared)));
            Assert.That(otherBounds, Is.EqualTo(s_bounds.Translate(s_other)));

            Assert.That(declaredRenderer.HitTest(s_content + s_declared), Is.True);
            Assert.That(otherRenderer.HitTest(s_content + s_other), Is.True);
            Assert.That(otherRenderer.HitTest(s_content + s_declared), Is.False);

            Assert.That(
                other.Call(s_other).Description.StructuralIdentity,
                Is.EqualTo(declared.Call(s_declared).Description.StructuralIdentity),
                "a contract is keyed by the callback it holds, so rebuilding around one parsed source "
                + "costs the author no second plan");
        }
    }

    private static ShaderDefinition<Vector> CreateDefinition(Vector offset)
        => ShaderDefinition<Vector>.WholeSource(
            s_source,
            RenderBoundsContract.Create(
                offset,
                static (value, bounds) => bounds.Translate(value),
                static (value, required) => required.Translate(-value)),
            static bindings =>
            {
                bindings.Uniform("dx", static value => value.X);
                bindings.Uniform("dy", static value => value.Y);
            },
            hitTest: RenderHitTestContract.Custom(
                offset,
                static (value, context, point) => context.Inputs[0].HitTest(point - value)));

    private static Vector BoundOffset(ShaderCall<Vector> call)
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
            ReadFloatUniform(call, "dx", execution),
            ReadFloatUniform(call, "dy", execution));
        token.Complete();
        return offset;
    }

    private static float ReadFloatUniform(
        ShaderCall<Vector> call,
        string name,
        ShaderExecutionContext execution)
    {
        ShaderUniformBinding binding = call.Description.Uniforms.Single(item => item.Name == name);
        return binding.Bind(call.Description.Source.Uniforms[name], execution).Floats![0];
    }

    private static OpaqueRenderCall<Color> SourceCall()
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
            .Call(s_fill);

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

    private sealed class TranslateNode(ShaderDefinition<Vector> definition, Vector offset) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(SourceCall());
            context.Publish(context.Shader(source, definition.Call(offset)));
        }
    }
}
