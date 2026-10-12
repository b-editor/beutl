using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
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

        ShaderDescription first = Tint(token, 0.25f);
        ShaderDescription second = Tint(token, 0.75f);

        // Two descriptions of the same structure are each bound with the value they declared.
        FilterEffectTestHarness.BindResource(first, new Rect(0, 0, 8, 8));
        FilterEffectTestHarness.BindResource(second, new Rect(0, 0, 8, 8));

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
            hitTestResources: [s_tintSlot.Bind(token)]);

    private sealed class TintProbe
    {
        public List<float> BoundAlphas { get; } = [];
    }
}
