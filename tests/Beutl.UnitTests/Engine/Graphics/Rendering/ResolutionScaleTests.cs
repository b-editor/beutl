using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Models;

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

    // At(scale) is a concrete density; it later divides a buffer footprint (EffectTarget.Draw), so a
    // zero/negative/non-finite value is rejected at the factory rather than failing silently downstream.
    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void EffectiveScale_At_RejectsNonPositiveOrNonFinite(float scale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EffectiveScale.At(scale));
    }

    // --- ResolveWorkingScale: the supply-driven core ---------------------------------
    // w = min( max(s_out, densest concrete supply), maxWorkingScale ). s_out is a FLOOR (an effect never runs
    // below the deliverable density), never a ceiling (a denser supply runs above it), bounded only by the global
    // memory ceiling. There is no resolution policy: an effect needing a different working scale overrides Process
    // in a FilterEffectRenderNode subclass returned from FilterEffect.Resource.CreateRenderNode().

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
    public void Resolve_SubOutputSupply_IsFlooredAtOutputScale()
    {
        // A sub-output concrete supply (an enlarged / low-density bitmap, At(0.5)) feeding an effect at a 1.0
        // export is floored to s_out: the effect's working resolution must not drop below the deliverable density
        // (that would discard resolution the target can use, matching the pre-feature renderer). w == max(1.0, 0.5) == 1.0.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(0.5f)],
            outputScale: 1.0f);
        Assert.That(w, Is.EqualTo(1.0f));
    }

    [Test]
    public void Resolve_ReducedScaleProxy_StaysCheapInPreview()
    {
        // A genuine reduced-scale proxy stays cheap: a 0.5 proxy at a 0.5 preview gives max(0.5, 0.5) == 0.5, no
        // forced upsample. The floor only lifts a supply below the current pull's output density, so reduced-scale
        // preview keeps its s² cost saving.
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(0.5f)],
            outputScale: 0.5f);
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
        // Preview memory ceiling (FR-037): a 4.0 source is capped to 2x the output. This is the only bound on the
        // working scale now that policies are gone — a high or transform-rescaled density can't blow past the ceiling.
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

    // --- CHARACTERIZATION: "densest input forces the whole boundary" footgun (not a fix, a pin) -----------

    [Test]
    public void Resolve_SmallHighDensitySiblingBesideLowDensity_RaisesWholeBoundary()
    {
        // CHARACTERIZATION (does NOT assert the footgun is fixed): w is the densest concrete input across the
        // WHOLE buffer-allocating boundary, so a single small high-density sibling (e.g. a 4K logo shrunk into a
        // corner, At(8)) beside a large low-density / vector input raises the working scale — and thus buffer
        // AREA (∝ w²) — of the ENTIRE boundary, not just its own region. This pins the known footgun
        // (effect-scale-contract.md "Footgun"); the documented follow-up is per-target (per-region) w scoping
        // and a request-scoped area budget. Until then, the densest input wins for everyone.
        float wWithVector = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(8f), EffectiveScale.At(1f), EffectiveScale.Unbounded],
            outputScale: 1f);
        Assert.That(wWithVector, Is.EqualTo(8f),
            "one small At(8) sibling lifts the whole boundary to w == 8 (the footgun this test pins)");

        // Even a single dense sibling next to pure vector content drags the boundary up to the dense density.
        float wDenseBesideVector = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(8f), EffectiveScale.Unbounded],
            outputScale: 1f);
        Assert.That(wDenseBesideVector, Is.EqualTo(8f),
            "the lone At(8) sibling forces w == 8 for the vector content too (∝ w² buffer-area cost)");
    }

    // --- PIN: supply-driven model's intentional NON-speedup on a source-heavy reduced-scale preview --------

    [Test]
    public void Resolve_HighDensitySourceInHalfPreview_RunsAtFullDensity_NoPreviewSpeedup()
    {
        // SC-003 source-heavy variant (the model's KNOWN non-speedup): a high-density source (At(4), e.g. a 4K
        // source on a reduced timeline) under an effect in a Half preview does NOT get the s²≈0.25 preview speedup.
        // w = min( max(s_out, supply), maxWorkingScale ) = min(max(0.5, 4), 1.0) = 1.0 — 4× the pixels of the 0.5
        // ideal. Intentional supply-driven tradeoff: the effect runs at the densest of floor and supply, capped only
        // by the preview ceiling (2 × s_out = 1.0 here), so a dense source under an effect stays expensive in preview.
        const float halfPreviewCeiling = 1.0f; // = WorkingScaleCeiling.Preview(0.5f) = 2 × 0.5
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(4f)],
            outputScale: 0.5f,
            maxWorkingScale: WorkingScaleCeiling.Preview(0.5f));
        Assert.That(WorkingScaleCeiling.Preview(0.5f), Is.EqualTo(halfPreviewCeiling).Within(1e-6),
            "Half preview ceiling must be 2 × s_out == 1.0 (the cap that bounds this non-speedup)");
        Assert.That(w, Is.EqualTo(1.0f),
            "a 4K source under an effect in Half preview runs at w == 1.0 (no s²≈0.25 speedup) — the intentional "
            + "supply-driven tradeoff: min(supply, 2·s_out), not the reduced 0.5 ideal");
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
        // Hard guarantee: the allocated axis ceil(8640 × w) must be <= the limit (no +1 float-rounding slack).
        Assert.That(Math.Ceiling(8640.0 * w), Is.LessThanOrEqualTo(RenderNodeContext.MaxBufferDimension));
    }

    [Test]
    public void ClampBudget_IsAHardGuarantee_AcrossFractionalBounds()
    {
        // Float narrowing of the fit factor previously let ceil(axis × w) land at MaxBufferDimension + 1 for
        // some fractional inputs. The clamp now steps the factor down until the buffer provably fits, so the
        // allocated axis is ALWAYS <= the limit. Probe fractional bounds/scales that trigger it.
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
        // It only ever reduces; a degenerate w passes through (the EffectiveScale.At factory already rejects
        // non-finite densities, but the clamp must not amplify or NaN-propagate).
        Assert.That(RenderNodeContext.ClampWorkingScaleToBufferBudget(new Rect(0, 0, 100, 100), 3.0f),
            Is.EqualTo(3.0f)); // fits => unchanged, never raised
        Assert.That(RenderNodeContext.ClampWorkingScaleToBufferBudget(new Rect(0, 0, 100, 100), float.NaN),
            Is.NaN);
    }

    [Test]
    public void ClampBudget_PostEffectInflation_NeedsReclampAtAllocationSite()
    {
        // FR-037: the node-level clamp runs against PRE-effect input bounds, but a blur/shadow inflates the bounds
        // by sigma×3 BEFORE the buffer is allocated in FilterEffectActivator.Flush. This is WHY Flush re-clamps
        // against the inflated OriginalBounds: a w that fits the input bounds can overflow once the effect inflates
        // them. Here: input fits at w=3, but after a sigma=2000 blur (Inflate by 3×sigma per side) the bounds blow
        // past the GPU limit and the re-clamp must shrink w.
        var inputBounds = new Rect(0, 0, 4000, 4000);
        float wAtInput = RenderNodeContext.ClampWorkingScaleToBufferBudget(inputBounds, 3.0f);
        Assert.That(wAtInput, Is.EqualTo(3.0f), "input bounds fit at w=3 — the node-level clamp is inert");

        // A large blur inflates each side by 3×sigma (FilterEffectContext.Blur), so the buffer that Flush
        // actually allocates is sized against these inflated bounds, not the input bounds.
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
        // All-vector path: supply = outputScale. A degenerate request scale must NOT flow through to a
        // non-finite / zero working scale (which would size a zero / NaN buffer downstream); it degrades to unit.
        ReadOnlySpan<EffectiveScale> vectorOnly = [EffectiveScale.Unbounded, EffectiveScale.Unbounded];
        float w = RenderNodeContext.ResolveWorkingScale(vectorOnly, badScale);
        Assert.That(w, Is.EqualTo(1f));
    }

    [Test]
    public void ResolveWorkingScale_DegenerateOutputScale_DoesNotDragDownAConcreteSupply()
    {
        // With a concrete supply present, the densest concrete input still wins; the sanitized outputScale only
        // affects the mixed-content floor, which can never exceed the (now unit) sanitized value.
        ReadOnlySpan<EffectiveScale> mixed = [EffectiveScale.At(2f), EffectiveScale.Unbounded];
        float w = RenderNodeContext.ResolveWorkingScale(mixed, float.NaN);
        Assert.That(w, Is.EqualTo(2f));
    }

    // --- s_out floor across supersample / preview outputs (the enlarge-regression fix) ------------------

    [TestCase(0.5f, 2.0f, 2.0f)] // sub-output proxy in a 2x SSAA export: floored to the deliverable 2.0
    [TestCase(1.0f, 2.0f, 2.0f)] // 1:1 source in a 2x SSAA export: floored to 2.0
    [TestCase(2.0f, 2.0f, 2.0f)] // supply matches the output
    [TestCase(2.0f, 1.0f, 2.0f)] // a 2.0 source in a 1.0 export: supply wins (floor inert) — s_out not a ceiling
    [TestCase(0.5f, 0.5f, 0.5f)] // a 0.5 proxy in a 0.5 preview: floor inert, reduced-scale stays cheap
    public void Resolve_ConcreteSupply_IsMaxOfSupplyAndOutput(float supply, float outputScale, float expected)
    {
        float w = RenderNodeContext.ResolveWorkingScale([EffectiveScale.At(supply)], outputScale);
        Assert.That(w, Is.EqualTo(expected).Within(1e-6));
    }

    // --- EffectiveScale.AtOrUnbounded: the non-throwing pull-path factory (plugin crash-trap fix) --------

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

    // --- RenderNodeContext sanitizes a degenerate request scale ONCE at the boundary --------------------
    // so a downstream consumer that reads OutputScale and calls At(w) (ParticleRenderNode / Scene3DRenderNode)
    // inherits a positive-finite density and never crashes the render on a 0 / NaN / ∞ request scale.

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

    // --- Shader device-buffer dimensions (the size SKSL/GLSL resolution uniforms must report) ------------

    [Test]
    public void DeviceBufferSize_MatchesCreateTargetFormula()
    {
        // The SKSL/GLSL resolution uniforms now bind these exact dimensions, so they must equal what
        // CreateTarget allocates: (int) truncation at w == 1 (byte-identity), ceil(bounds × w) at w != 1.
        Assert.That(CustomFilterEffectContext.DeviceBufferSize(new Rect(0, 0, 100.7f, 50.2f), 1f),
            Is.EqualTo((100, 50)), "w == 1 truncates (matches the byte-identity (int) cast)");
        Assert.That(CustomFilterEffectContext.DeviceBufferSize(new Rect(0, 0, 100.0f, 50.0f), 2f),
            Is.EqualTo((200, 100)), "integral bounds × w stays integral");
        Assert.That(CustomFilterEffectContext.DeviceBufferSize(new Rect(0, 0, 100.3f, 50.1f), 2f),
            Is.EqualTo((201, 101)), "fractional bounds × w ceil()s up — the case the un-ceiled uniform got wrong");
    }

    // --- Node-graph input boundary: EffectiveScale must survive the RefCountedProxy re-wrap ---------------

    [Test]
    public void OperationWrapperProxy_ForwardsEffectiveScale()
    {
        // OperationWrapperRenderNode wraps each op in a RefCountedProxy for the node-graph input boundary. The
        // proxy applies no geometric transform, so it must forward the wrapped op's supply density verbatim —
        // otherwise a node-graph filter fed a concrete-density input would see Unbounded and rasterize at s_out.
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
