using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

/// <summary>
/// Pins that a shader definition's resource binder can be supplied from the call state.
/// </summary>
/// <remarks>
/// Every other binding a definition declares reads its value out of the call state, and a binder has to be
/// non-capturing, so before this the resource binder was the one shader input that could not depend on the
/// call it was recorded for. The state has to arrive as an argument or it cannot arrive at all.
/// </remarks>
[TestFixture]
public sealed class ShaderDefinitionResourceStateTests
{
    private const string TintSource =
        "uniform shader src; uniform shader tint; half4 main(float2 coord) { return tint.eval(coord); }";

    private static readonly RenderResourceSlot<TintProbe> s_tintSlot = new();

    private static readonly ShaderDefinition<TintRequest> s_definition =
        ShaderDefinition<TintRequest>.WholeSource(
            TintSource,
            RenderBoundsContract.Identity,
            static bindings => bindings.Resource(
                "tint",
                s_tintSlot,
                ShaderResourceCoordinateSpace.OutputDevice,
                static request => request.Alpha,
                static (writer, probe, alpha, _) =>
                {
                    probe.BoundAlphas.Add(alpha);
                    writer.Set(SKShader.CreateColor(new SKColor(255, 255, 255, (byte)(alpha * 255))));
                }));

    [Test]
    public void AResourceBinderReadsTheStateItsCallWasMadeWith()
    {
        using var registry = new RenderRequestResourceRegistry();
        var probe = new TintProbe();
        RenderResource<TintProbe> token = registry.RegisterBorrowed(probe);
        registry.Commit(token);
        var request = new TintRequest { Alpha = 0.25f };

        ShaderCall<TintRequest> first = s_definition.Call(request, [s_tintSlot.Bind(token)]);
        request.Alpha = 0.75f;
        ShaderCall<TintRequest> second = s_definition.Call(request, [s_tintSlot.Bind(token)]);

        // Both calls are recorded before either binder runs, so a binder reading the live state here would
        // see the last value twice instead of the value its own call was made with.
        request.Alpha = 1f;
        BindResource(first);
        BindResource(second);

        Assert.Multiple(() =>
        {
            Assert.That(probe.BoundAlphas, Is.EqualTo(new[] { 0.25f, 0.75f }));
            Assert.That(
                second.Description.StructuralIdentity,
                Is.EqualTo(first.Description.StructuralIdentity),
                "The call state is not part of the plan key, so two calls of one definition still fuse.");
        });
    }

    private static void BindResource(ShaderCall<TintRequest> call)
    {
        var token = new RenderExecutionSessionToken();
        var execution = new ShaderExecutionContext(
            token,
            new Rect(0, 0, 8, 8),
            new Rect(0, 0, 8, 8),
            new Rect(0, 0, 8, 8),
            new PixelRect(0, 0, 8, 8),
            EffectiveScale.At(1),
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 2,
            intent: RenderIntent.Preview,
            purpose: RenderRequestPurpose.Frame);

        call.Description.Resources.Single().Bind(execution).Dispose();
        token.Complete();
    }

    private sealed class TintRequest
    {
        public float Alpha { get; set; }
    }

    private sealed class TintProbe
    {
        public List<float> BoundAlphas { get; } = [];
    }
}
