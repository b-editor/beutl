using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Models;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// Pure-math tests for the supply-driven scale model. No GPU required.
[TestFixture]
public class ResolutionScaleTests
{
    [Test]
    public void EffectiveScale_Default_IsUnbounded()
    {
        EffectiveScale e = default;
        Assert.That(e.IsUnbounded, Is.True);
        Assert.That(e, Is.EqualTo(EffectiveScale.Unbounded));
        // Value is the neutral 1f for unbounded (vector) supply.
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

    // At(scale) rejects zero/negative/non-finite values at the factory.
    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void EffectiveScale_At_RejectsNonPositiveOrNonFinite(float scale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EffectiveScale.At(scale));
    }

    // --- ResolveWorkingScale: w = min(max(s_out, densest supply), maxWorkingScale) ---

    [Test]
    public void Resolve_AllVectorInputs_RastersAtOutputScale()
    {
        // All-vector: rasterize at the output density.
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
    public void Resolve_SubOutputSupply_IsFlooredAtOutputScale()
    {
        // Sub-output supply floored to s_out: w = max(1.0, 0.5) = 1.0.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(0.5f)],
            outputScale: 1.0f);
        Assert.That(w, Is.EqualTo(1.0f));
    }

    [Test]
    public void Resolve_ReducedScaleProxy_StaysCheapInPreview()
    {
        // A 0.5 proxy at 0.5 preview gives max(0.5, 0.5) = 0.5, no forced upsample.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(0.5f)],
            outputScale: 0.5f);
        Assert.That(w, Is.EqualTo(0.5f));
    }

    [Test]
    public void Resolve_HighResSource_IsNotClampedByOutput()
    {
        // A 2.0 source at 1.0 output keeps its density. Output is not a ceiling.
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
        // Preview ceiling: a 4.0 source is capped to 2x the output.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(4.0f)],
            outputScale: 1.0f,
            maxWorkingScale: 2.0f);
        Assert.That(w, Is.EqualTo(2.0f));
    }

    // --- Mixed bitmap + vector must not drag vector down to bitmap's density ---

    [Test]
    public void Resolve_LowResBitmapWithVector_FloorsAtOutput()
    {
        // A 0.5 bitmap beside vector must not pull w to 0.5; vector can draw at s_out.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(0.5f), EffectiveScale.Unbounded],
            outputScale: 1.0f);
        Assert.That(w, Is.EqualTo(1.0f));
    }

    [Test]
    public void Resolve_HighResBitmapWithVector_KeepsBitmapDensity()
    {
        // A 2.0 bitmap alongside vector keeps the densest supply (2.0), not pulled to 1.0.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(2.0f), EffectiveScale.Unbounded],
            outputScale: 1.0f);
        Assert.That(w, Is.EqualTo(2.0f));
    }

    [Test]
    public void Resolve_UnitBitmapWithVector_IsByteIdentityNeutral()
    {
        // At(1) + vector at output 1.0 => w == 1.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(1.0f), EffectiveScale.Unbounded],
            outputScale: 1.0f);
        Assert.That(w, Is.EqualTo(1.0f));
    }

    [Test]
    public void Resolve_UnitInputsUnitOutput_IsOne()
    {
        // Unit supply at output 1.0 => w == 1.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.Unbounded],
            outputScale: 1.0f);
        Assert.That(w, Is.EqualTo(1.0f));
    }

    // --- CHARACTERIZATION: "densest input forces the whole boundary" (pinning known behavior) ---

    [Test]
    public void Resolve_SmallHighDensitySiblingBesideLowDensity_RaisesWholeBoundary()
    {
        // CHARACTERIZATION: the densest input raises w for the entire boundary (known footgun, pinned here).
        float wWithVector = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(8f), EffectiveScale.At(1f), EffectiveScale.Unbounded],
            outputScale: 1f);
        Assert.That(wWithVector, Is.EqualTo(8f),
            "one small At(8) sibling lifts the whole boundary to w == 8");

        // Even a single dense sibling next to pure vector content raises w.
        float wDenseBesideVector = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(8f), EffectiveScale.Unbounded],
            outputScale: 1f);
        Assert.That(wDenseBesideVector, Is.EqualTo(8f),
            "the lone At(8) sibling forces w == 8 for the vector content too");
    }

    // --- PIN: supply-driven model's intentional non-speedup on source-heavy reduced-scale preview ---

    [Test]
    public void Resolve_HighDensitySourceInHalfPreview_RunsAtFullDensity_NoPreviewSpeedup()
    {
        // A high-density source under an effect in Half preview does not get the reduced-scale speedup.
        // w = min(max(0.5, 4), 1.0) = 1.0, capped by the preview ceiling (2 * s_out).
        const float halfPreviewCeiling = 1.0f; // = WorkingScaleCeiling.Preview(0.5f) = 2 × 0.5
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(4f)],
            outputScale: 0.5f,
            maxWorkingScale: WorkingScaleCeiling.Preview(0.5f));
        Assert.That(WorkingScaleCeiling.Preview(0.5f), Is.EqualTo(halfPreviewCeiling).Within(1e-6),
            "Half preview ceiling must be 2 * s_out == 1.0");
        Assert.That(w, Is.EqualTo(1.0f),
            "a high-density source under an effect in Half preview runs at w == 1.0, not the reduced 0.5 ideal");
    }

    // --- Buffer-budget backstop (GPU-texture limit) ---

    [Test]
    public void ClampBudget_SmallBuffer_LeavesScaleUnchanged()
    {
        // Common case: a buffer within the GPU limit is untouched.
        float w = RenderNodeContext.ClampWorkingScaleToBufferBudget(new Rect(0, 0, 1920, 1080), 2.0f);
        Assert.That(w, Is.EqualTo(2.0f));
    }

    [Test]
    public void ClampBudget_AnisotropicOverAllocation_IsBounded()
    {
        // Anisotropic case: ceil(8640 * 4) = 34560 > 16384, must clamp so the larger axis fits.
        float w = RenderNodeContext.ClampWorkingScaleToBufferBudget(new Rect(0, 0, 960, 8640), 4.0f);
        Assert.That(w, Is.LessThan(4.0f), "anisotropic density must be clamped to fit the GPU buffer limit");
        // Hard guarantee: ceil(8640 * w) must be <= the limit.
        Assert.That(Math.Ceiling(8640.0 * w), Is.LessThanOrEqualTo(RenderNodeContext.MaxBufferDimension));
    }

    [Test]
    public void ClampBudget_IsAHardGuarantee_AcrossFractionalBounds()
    {
        // Probe fractional bounds/scales that previously let ceil(axis * w) exceed the limit by 1.
        foreach (float axis in new[] { 5000.3f, 8640.7f, 12000.1f, 16384.9f, 20001.5f, 33333.33f })
        {
            foreach (float w in new[] { 1.7f, 3.3f, 4.0f, 7.9f, 12.5f })
            {
                var bounds = new Rect(0, 0, axis, axis * 0.5f);
                float clamped = RenderNodeContext.ClampWorkingScaleToBufferBudget(bounds, w);
                double allocatedAxis = Math.Ceiling((double)axis * clamped);
                Assert.That(allocatedAxis, Is.LessThanOrEqualTo(RenderNodeContext.MaxBufferDimension),
                    $"axis={axis}, w={w}: allocated {allocatedAxis} px must fit the GPU limit exactly");
                Assert.That(clamped, Is.LessThanOrEqualTo(w), "the clamp must never raise the scale");
            }
        }
    }

    [Test]
    public void ClampBudget_NeverIncreasesScale_AndGuardsNonFinite()
    {
        // The clamp only ever reduces; a degenerate w passes through without amplification.
        Assert.That(RenderNodeContext.ClampWorkingScaleToBufferBudget(new Rect(0, 0, 100, 100), 3.0f),
            Is.EqualTo(3.0f)); // fits => unchanged, never raised
        Assert.That(RenderNodeContext.ClampWorkingScaleToBufferBudget(new Rect(0, 0, 100, 100), float.NaN),
            Is.NaN);
    }

    [Test]
    public void ClampBudget_PostEffectInflation_NeedsReclampAtAllocationSite()
    {
        // The node-level clamp runs against pre-effect bounds, but effect inflation (blur/shadow) can
        // overflow the GPU limit, so Flush re-clamps against inflated bounds.
        var inputBounds = new Rect(0, 0, 4000, 4000);
        float wAtInput = RenderNodeContext.ClampWorkingScaleToBufferBudget(inputBounds, 3.0f);
        Assert.That(wAtInput, Is.EqualTo(3.0f), "input bounds fit at w=3 — the node-level clamp is inert");

        // A large blur inflates each side by 3*sigma; Flush allocates against inflated bounds.
        Rect inflated = inputBounds.Inflate(new Thickness(3 * 2000, 3 * 2000));
        float wAtAllocation = RenderNodeContext.ClampWorkingScaleToBufferBudget(inflated, 3.0f);
        Assert.That(wAtAllocation, Is.LessThan(wAtInput),
            "the re-clamp against post-inflation bounds must reduce w so the inflated buffer stays allocatable");
        double largestAxis = Math.Max(inflated.Width, inflated.Height);
        Assert.That(Math.Ceiling(largestAxis * wAtAllocation), Is.LessThanOrEqualTo(RenderNodeContext.MaxBufferDimension),
            "post-inflation buffer must fit the GPU dimension limit after the allocation-site re-clamp");
    }

    // --- ResolveWorkingScale defensive guard (degenerate output scale) ----------------------------------

    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void ResolveWorkingScale_DegenerateOutputScale_DegradesToUnitOnVectorPath(float badScale)
    {
        // All-vector path: degenerate request scale degrades to unit (prevents zero/NaN buffers).
        ReadOnlySpan<EffectiveScale> vectorOnly = [EffectiveScale.Unbounded, EffectiveScale.Unbounded];
        float w = RenderNodeContext.ResolveWorkingScale(vectorOnly, badScale);
        Assert.That(w, Is.EqualTo(1f));
    }

    [Test]
    public void ResolveWorkingScale_DegenerateOutputScale_DoesNotDragDownAConcreteSupply()
    {
        // With a concrete supply, the densest input still wins regardless of the sanitized output scale.
        ReadOnlySpan<EffectiveScale> mixed = [EffectiveScale.At(2f), EffectiveScale.Unbounded];
        float w = RenderNodeContext.ResolveWorkingScale(mixed, float.NaN);
        Assert.That(w, Is.EqualTo(2f));
    }

    // --- s_out floor across supersample / preview outputs ---

    [TestCase(0.5f, 2.0f, 2.0f)] // sub-output proxy floored to 2.0
    [TestCase(1.0f, 2.0f, 2.0f)] // 1:1 source floored to 2.0
    [TestCase(2.0f, 2.0f, 2.0f)] // supply matches output
    [TestCase(2.0f, 1.0f, 2.0f)] // supply wins, s_out not a ceiling
    [TestCase(0.5f, 0.5f, 0.5f)] // 0.5 proxy at 0.5 preview: floor inert
    public void Resolve_ConcreteSupply_IsMaxOfSupplyAndOutput(float supply, float outputScale, float expected)
    {
        float w = RenderNodeContext.ResolveWorkingScale([EffectiveScale.At(supply)], outputScale);
        Assert.That(w, Is.EqualTo(expected).Within(1e-6));
    }

    // --- EffectiveScale.AtOrUnbounded: non-throwing factory for pull paths ---

    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void AtOrUnbounded_DegradesBadDensityToUnbounded(float bad)
    {
        Assert.That(EffectiveScale.AtOrUnbounded(bad), Is.EqualTo(EffectiveScale.Unbounded));
        Assert.That(EffectiveScale.AtOrUnbounded(bad).IsUnbounded, Is.True);
    }

    [Test]
    public void AtOrUnbounded_KeepsAValidDensity()
    {
        Assert.That(EffectiveScale.AtOrUnbounded(0.5f), Is.EqualTo(EffectiveScale.At(0.5f)));
    }

    // --- RenderNodeContext sanitizes degenerate request scale at the boundary ---

    [TestCase(0f)]
    [TestCase(-2f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void RenderNodeContext_SanitizesDegenerateOutputScaleToOne(float bad)
    {
        var ctx = new RenderNodeContext([], outputScale: bad);
        Assert.That(ctx.OutputScale, Is.EqualTo(1f));
    }

    [TestCase(float.NaN)]
    [TestCase(0f)]
    [TestCase(-1f)]
    public void RenderNodeContext_DegenerateMaxWorkingScale_IsTreatedAsNoCeiling(float bad)
    {
        var ctx = new RenderNodeContext([], outputScale: 1f, maxWorkingScale: bad);
        Assert.That(ctx.MaxWorkingScale, Is.EqualTo(float.PositiveInfinity));
        // and it must not pull a resolved working scale to zero / NaN
        float w = RenderNodeContext.ResolveWorkingScale([EffectiveScale.At(3f)], 1f, ctx.MaxWorkingScale);
        Assert.That(w, Is.EqualTo(3f));
    }

    [TestCase(float.NaN)]
    [TestCase(0f)]
    [TestCase(-1f)]
    public void RenderNodeProcessor_DegenerateMaxWorkingScale_IsTreatedAsNoCeiling(float bad)
    {
        using var node = new OperationWrapperRenderNode();
        var processor = new RenderNodeProcessor(node, false, 1f, bad);
        Assert.That(processor.MaxWorkingScale, Is.EqualTo(float.PositiveInfinity));
    }

    [TestCase(float.NaN)]
    [TestCase(0f)]
    [TestCase(-1f)]
    public void RenderNodeProcessor_DegenerateOutputScale_DefaultsToOne(float bad)
    {
        using var node = new OperationWrapperRenderNode();
        var processor = new RenderNodeProcessor(node, false, bad);
        Assert.That(processor.OutputScale, Is.EqualTo(1f));
    }

    // --- Shader device-buffer dimensions (the size SKSL/GLSL resolution uniforms must report) ------------

    [Test]
    public void DeviceBufferSize_MatchesCreateTargetFormula()
    {
        // Must match CreateTarget: (int) truncation at w==1, ceil(bounds * w) at w!=1.
        Assert.That(CustomFilterEffectContext.DeviceBufferSize(new Rect(0, 0, 100.7f, 50.2f), 1f),
            Is.EqualTo((100, 50)), "w == 1 truncates");
        Assert.That(CustomFilterEffectContext.DeviceBufferSize(new Rect(0, 0, 100.0f, 50.0f), 2f),
            Is.EqualTo((200, 100)), "integral bounds * w stays integral");
        Assert.That(CustomFilterEffectContext.DeviceBufferSize(new Rect(0, 0, 100.3f, 50.1f), 2f),
            Is.EqualTo((201, 101)), "fractional bounds * w ceils up");
    }

    // --- Flatten nodes own no buffer and re-rasterize at any scale: must report Unbounded supply ---------

    [Test]
    public void LayerRenderNode_Process_EmitsUnboundedEffectiveScale()
    {
        // SaveLayer flatten owns no buffer and re-rasterizes at any working scale, so it must report
        // Unbounded supply density; a wrongly-concrete value would inflate the upstream working scale.
        using var node = new LayerRenderNode(new Rect(0, 0, 100, 100));

        RenderNodeOperation[] result = node.Process(new RenderNodeContext([]));
        try
        {
            Assert.That(result, Has.Length.EqualTo(1));
            Assert.That(result[0].EffectiveScale.IsUnbounded, Is.True,
                "LayerRenderNode flattens via SaveLayer and re-rasterizes at any scale, so it emits Unbounded.");
        }
        finally
        {
            foreach (RenderNodeOperation r in result)
                r.Dispose();
        }
    }

    // --- Node-graph input boundary: EffectiveScale must survive the RefCountedProxy re-wrap ---------------

    [Test]
    public void OperationWrapperProxy_ForwardsEffectiveScale()
    {
        // The proxy must forward the wrapped op's supply density verbatim.
        using var node = new OperationWrapperRenderNode();
        var op = RenderNodeOperation.CreateLambda(
            new Rect(0, 0, 10, 10),
            render: _ => { },
            effectiveScale: EffectiveScale.At(0.5f));
        node.SetOperations([op]);

        RenderNodeOperation[] result = node.Process(new RenderNodeContext([]));
        try
        {
            Assert.That(result, Has.Length.EqualTo(1));
            Assert.That(result[0].EffectiveScale.IsUnbounded, Is.False,
                "the proxy must not collapse a concrete supply density to Unbounded");
            Assert.That(result[0].EffectiveScale.Value, Is.EqualTo(0.5f));
        }
        finally
        {
            foreach (RenderNodeOperation r in result)
                r.Dispose();
        }
    }
}
