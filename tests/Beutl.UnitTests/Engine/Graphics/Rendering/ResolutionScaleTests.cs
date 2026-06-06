using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// Pure-math tests for the supply-driven scale model (feature 003). No GPU required.
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

    [Test]
    public void ResolutionPolicy_Default_IsInherit()
    {
        ResolutionPolicy p = default;
        Assert.That(p.Kind, Is.EqualTo(ResolutionPolicyKind.Inherit));
        Assert.That(p, Is.EqualTo(ResolutionPolicy.Inherit));
    }

    [Test]
    public void ResolutionPolicy_Oversample_CarriesFactor()
    {
        ResolutionPolicy p = ResolutionPolicy.Oversample(2f);
        Assert.That(p.Kind, Is.EqualTo(ResolutionPolicyKind.Oversample));
        Assert.That(p.Factor, Is.EqualTo(2f));
    }

    // A zero/negative Oversample factor would resolve identically to Inherit — reject it at the factory rather
    // than silently degrading (D2).
    [TestCase(0f)]
    [TestCase(-1f)]
    public void ResolutionPolicy_Oversample_RejectsNonPositiveFactor(float factor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ResolutionPolicy.Oversample(factor));
    }

    // --- ResolveWorkingScale: the supply-driven core ---------------------------------

    [Test]
    public void Resolve_AllVectorInputs_RastersAtOutputScale()
    {
        // No concrete supply => vector content => rasterize at the output density.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.Unbounded, EffectiveScale.Unbounded],
            outputScale: 0.5f,
            ResolutionPolicy.Inherit);
        Assert.That(w, Is.EqualTo(0.5f));
    }

    [Test]
    public void Resolve_NoInputs_RastersAtOutputScale()
    {
        float w = RenderNodeContext.ResolveWorkingScale([], outputScale: 1.5f, ResolutionPolicy.Inherit);
        Assert.That(w, Is.EqualTo(1.5f));
    }

    [Test]
    public void Resolve_Inherit_LowResProxy_IsNotUpsampled()
    {
        // R1: a 0.5 proxy input must NOT be upsampled to the 1.0 output. w stays 0.5.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(0.5f)],
            outputScale: 1.0f,
            ResolutionPolicy.Inherit);
        Assert.That(w, Is.EqualTo(0.5f));
    }

    [Test]
    public void Resolve_Inherit_HighResSource_IsNotClampedByOutput()
    {
        // R2: a 2.0 source feeding a 1.0 timeline keeps its density into the effect. Output is not a ceiling.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(2.0f)],
            outputScale: 1.0f,
            ResolutionPolicy.Inherit);
        Assert.That(w, Is.EqualTo(2.0f));
    }

    [Test]
    public void Resolve_Inherit_MixedConcrete_TakesDensestSupply()
    {
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(0.5f), EffectiveScale.At(2.0f), EffectiveScale.Unbounded],
            outputScale: 1.0f,
            ResolutionPolicy.Inherit);
        Assert.That(w, Is.EqualTo(2.0f));
    }

    [Test]
    public void Resolve_ClampToOutput_CapsHighSource()
    {
        // Perf opt-out: a 2.0 source is clamped down to the 1.0 output.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(2.0f)],
            outputScale: 1.0f,
            ResolutionPolicy.ClampToOutput);
        Assert.That(w, Is.EqualTo(1.0f));
    }

    [Test]
    public void Resolve_ClampToOutput_DoesNotUpsampleLowSource()
    {
        // Clamp is a ceiling, not a floor: a 0.5 proxy is not raised to 1.0.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(0.5f)],
            outputScale: 1.0f,
            ResolutionPolicy.ClampToOutput);
        Assert.That(w, Is.EqualTo(0.5f));
    }

    [Test]
    public void Resolve_ClampToOutput_FlooredByPreserveSourceAncestor()
    {
        // A PreserveSource ancestor floors a ClampToOutput so a high source density survives.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(2.0f)],
            outputScale: 1.0f,
            ResolutionPolicy.ClampToOutput,
            preserveFloor: 2.0f);
        Assert.That(w, Is.EqualTo(2.0f));
    }

    [Test]
    public void Resolve_Oversample_ForcesAboveOutputEvenFromLowSource()
    {
        // SSAA on demand: 2x the 1.0 output even though the supply is only 0.5.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(0.5f)],
            outputScale: 1.0f,
            ResolutionPolicy.Oversample(2f));
        Assert.That(w, Is.EqualTo(2.0f));
    }

    [Test]
    public void Resolve_Oversample_KeepsHigherSupplyWhenSupplyExceedsTarget()
    {
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(3.0f)],
            outputScale: 1.0f,
            ResolutionPolicy.Oversample(2f));
        Assert.That(w, Is.EqualTo(3.0f));
    }

    [Test]
    public void Resolve_PreserveSource_KeepsSourceDensity()
    {
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(2.0f)],
            outputScale: 1.0f,
            ResolutionPolicy.PreserveSource);
        Assert.That(w, Is.EqualTo(2.0f));
    }

    [Test]
    public void Resolve_MaxWorkingScale_CapsResult()
    {
        // Preview memory ceiling (FR-037): a 4.0 source is capped to 2x the output.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(4.0f)],
            outputScale: 1.0f,
            ResolutionPolicy.Inherit,
            preserveFloor: 0f,
            maxWorkingScale: 2.0f);
        Assert.That(w, Is.EqualTo(2.0f));
    }

    [Test]
    public void Resolve_UnitInputsUnitOutput_IsOne()
    {
        // The byte-identity anchor: unit-scale supply at output 1.0 => w == 1.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.Unbounded],
            outputScale: 1.0f,
            ResolutionPolicy.Inherit);
        Assert.That(w, Is.EqualTo(1.0f));
    }
}
