using Beutl.Graphics;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// Pure-math tests for the supply-driven scale model (feature 003). No GPU required.
[TestFixture]
public class ResolutionScaleTests
{
    [Test]
    public void EffectiveScale_Default_IsUnbounded()
    {
        EffectiveScale e = default;
        Assert.That(e.IsUnbounded, Is.True);
        Assert.That(e, Is.EqualTo(EffectiveScale.Unbounded));
        // Value is the neutral 1f for an unbounded (vector) supply.
        Assert.That(e.Value, Is.EqualTo(1f));
    }

    [Test]
    public void EffectiveScale_At_IsConcrete()
    {
        EffectiveScale e = EffectiveScale.At(0.5f);
        Assert.That(e.IsUnbounded, Is.False);
        Assert.That(e.Value, Is.EqualTo(0.5f));
        Assert.That(e, Is.Not.EqualTo(EffectiveScale.Unbounded));
        Assert.That(e, Is.EqualTo(EffectiveScale.At(0.5f)));
    }

    // At(scale) is a concrete density; a zero/negative/non-finite value has no meaning and would later divide a
    // buffer footprint by it (EffectTarget.Draw) — reject it at the factory so it can't fail silently downstream.
    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void EffectiveScale_At_RejectsNonPositiveOrNonFinite(float scale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EffectiveScale.At(scale));
    }

    // --- ResolveWorkingScale: the supply-driven core ---------------------------------
    // There is no resolution policy: every boundary runs at the supply density, bounded only by the global
    // memory ceiling. (The former Inherit/ClampToOutput/Oversample policy was removed — an effect needing a
    // different working scale overrides FilterEffectRenderNode.ResolveWorkingScale instead.)

    [Test]
    public void Resolve_AllVectorInputs_RastersAtOutputScale()
    {
        // No concrete supply => vector content => rasterize at the output density.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.Unbounded, EffectiveScale.Unbounded],
            outputScale: 0.5f);
        Assert.That(w, Is.EqualTo(0.5f));
    }

    [Test]
    public void Resolve_NoInputs_RastersAtOutputScale()
    {
        float w = RenderNodeContext.ResolveWorkingScale([], outputScale: 1.5f);
        Assert.That(w, Is.EqualTo(1.5f));
    }

    [Test]
    public void Resolve_LowResProxy_IsNotUpsampled()
    {
        // R1: a 0.5 proxy input must NOT be upsampled to the 1.0 output. w stays 0.5.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(0.5f)],
            outputScale: 1.0f);
        Assert.That(w, Is.EqualTo(0.5f));
    }

    [Test]
    public void Resolve_HighResSource_IsNotClampedByOutput()
    {
        // R2: a 2.0 source feeding a 1.0 timeline keeps its density into the effect. Output is not a ceiling.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(2.0f)],
            outputScale: 1.0f);
        Assert.That(w, Is.EqualTo(2.0f));
    }

    [Test]
    public void Resolve_MixedConcrete_TakesDensestSupply()
    {
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(0.5f), EffectiveScale.At(2.0f), EffectiveScale.Unbounded],
            outputScale: 1.0f);
        Assert.That(w, Is.EqualTo(2.0f));
    }

    [Test]
    public void Resolve_MaxWorkingScale_CapsResult()
    {
        // Preview memory ceiling (FR-037): a 4.0 source is capped to 2x the output. This is the ONLY bound on
        // the working scale now that policies are gone — a high or transform-rescaled density can never blow up
        // the buffer past the ceiling.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(4.0f)],
            outputScale: 1.0f,
            maxWorkingScale: 2.0f);
        Assert.That(w, Is.EqualTo(2.0f));
    }

    // --- C4/C5: mixed bitmap + vector must not drag the vector down to the bitmap's density ----------

    [Test]
    public void Resolve_LowResBitmapWithVector_FloorsAtOutput()
    {
        // C4/C5: a 0.5 proxy bitmap sharing the boundary with crisp vector content must NOT pull the
        // working scale to 0.5 — the vector half can draw at the output density, so w floors at s_out.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(0.5f), EffectiveScale.Unbounded],
            outputScale: 1.0f);
        Assert.That(w, Is.EqualTo(1.0f));
    }

    [Test]
    public void Resolve_HighResBitmapWithVector_KeepsBitmapDensity()
    {
        // The vector floor is only a floor: a 2.0 bitmap alongside vector keeps the densest supply (2.0),
        // it is not pulled DOWN to the 1.0 output.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(2.0f), EffectiveScale.Unbounded],
            outputScale: 1.0f);
        Assert.That(w, Is.EqualTo(2.0f));
    }

    [Test]
    public void Resolve_UnitBitmapWithVector_IsByteIdentityNeutral()
    {
        // The byte-identity anchor must survive the C4/C5 floor: At(1) + vector at output 1.0 => w == 1.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(1.0f), EffectiveScale.Unbounded],
            outputScale: 1.0f);
        Assert.That(w, Is.EqualTo(1.0f));
    }

    [Test]
    public void Resolve_UnitInputsUnitOutput_IsOne()
    {
        // The byte-identity anchor: unit-scale supply at output 1.0 => w == 1.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.Unbounded],
            outputScale: 1.0f);
        Assert.That(w, Is.EqualTo(1.0f));
    }

    // --- Buffer-budget backstop (FR-037 memory / GPU-texture limit; Codex finding #1/#4) ---------------

    [Test]
    public void ClampBudget_SmallBuffer_LeavesScaleUnchanged()
    {
        // The common case: a buffer well within the GPU limit is untouched (byte-identity preserved).
        float w = RenderNodeContext.ClampWorkingScaleToBufferBudget(new Rect(0, 0, 1920, 1080), 2.0f);
        Assert.That(w, Is.EqualTo(2.0f));
    }

    [Test]
    public void ClampBudget_AnisotropicOverAllocation_IsBounded()
    {
        // FR-019 anisotropic case: a 3840×2160 source under a (0.25, 4) transform becomes 960×8640 logical,
        // projected to density At(4). ceil(8640 × 4) = 34560 px > 16384 → must clamp so the larger axis fits.
        float w = RenderNodeContext.ClampWorkingScaleToBufferBudget(new Rect(0, 0, 960, 8640), 4.0f);
        Assert.That(w, Is.LessThan(4.0f), "anisotropic density must be clamped to fit the GPU buffer limit");
        Assert.That(8640.0 * w, Is.LessThanOrEqualTo(RenderNodeContext.MaxBufferDimension + 1));
    }

    [Test]
    public void ClampBudget_NeverIncreasesScale_AndGuardsNonFinite()
    {
        // It only ever reduces; a degenerate w passes through (the EffectiveScale.At factory already rejects
        // non-finite densities, but the clamp must not amplify or NaN-propagate).
        Assert.That(RenderNodeContext.ClampWorkingScaleToBufferBudget(new Rect(0, 0, 100, 100), 3.0f),
            Is.EqualTo(3.0f)); // fits => unchanged, never raised
        Assert.That(RenderNodeContext.ClampWorkingScaleToBufferBudget(new Rect(0, 0, 100, 100), float.NaN),
            Is.NaN);
    }
}
