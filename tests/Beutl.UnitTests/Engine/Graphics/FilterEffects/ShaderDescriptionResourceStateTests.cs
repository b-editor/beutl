using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class ShaderDescriptionResourceStateTests
{
    private const string TintSource =
        "uniform shader src; uniform shader tint; half4 main(float2 coord) { return tint.eval(coord); }";

    private static readonly RenderResourceSlot<TintProbe> s_tintSlot = new();

    [Test]
    public void AResourceBinderReadsTheValueItsDescriptionDeclared()
    {
        using var registry = new RenderRequestResourceRegistry();
        var probe = new TintProbe();
        RenderResource<TintProbe> token = registry.RegisterBorrowed(probe);
        registry.Commit(token);
        var request = new TintRequest { Alpha = 0.25f };

        ShaderDescription first = Tint(token, request.Alpha);
        request.Alpha = 0.75f;
        ShaderDescription second = Tint(token, request.Alpha);

        // Both descriptions are built before either binder runs, so a binder reading the live state here
        // would see the last value twice instead of the value its own description declared.
        request.Alpha = 1f;
        BindResource(first);
        BindResource(second);

        Assert.Multiple(() =>
        {
            Assert.That(probe.BoundAlphas, Is.EqualTo(new[] { 0.25f, 0.75f }));
            Assert.That(
                second.StructuralIdentity,
                Is.EqualTo(first.StructuralIdentity),
                "The declared value is not part of the plan key, so two descriptions still fuse.");
        });
    }

    private static ShaderDescription Tint(RenderResource<TintProbe> token, float alpha)
        => ShaderDescription.WholeSource(
            TintSource,
            RenderBoundsContract.Identity,
            bindings => bindings.Resource(
                "tint",
                token,
                ShaderResourceCoordinateSpace.OutputDevice,
                alpha,
                static (writer, probe, value, _) =>
                {
                    probe.BoundAlphas.Add(value);
                    writer.Set(SKShader.CreateColor(new SKColor(255, 255, 255, (byte)(value * 255))));
                }),
            slots: [s_tintSlot],
            hitTestResources: [s_tintSlot.Bind(token)]);

    private static void BindResource(ShaderDescription description)
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

        description.Resources.Single().Bind(execution).Dispose();
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
