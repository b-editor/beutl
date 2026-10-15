using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Models;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// Scale-model tests that need no GPU.
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

    [TestCase(2, 0.5f, TestName = "Resolve_AllVectorInputs_RastersAtOutputScale")]
    [TestCase(1, 1.0f, TestName = "Resolve_UnitInputsUnitOutput_IsOne")]
    public void Resolve_VectorInputs_RasterAtOutputScale(int vectorInputs, float outputScale)
    {
        float w = RenderScaleUtilities.ResolveWorkingScale(
            Enumerable.Repeat(EffectiveScale.Unbounded, vectorInputs).ToArray(),
            outputScale);
        Assert.That(w, Is.EqualTo(outputScale));
    }

    [Test]
    public void Resolve_NoInputs_RastersAtOutputScale()
    {
        float w = RenderScaleUtilities.ResolveWorkingScale([], outputScale: 1.5f);
        Assert.That(w, Is.EqualTo(1.5f));
    }

    [Test]
    public void Resolve_MixedConcrete_TakesDensestSupply()
    {
        float w = RenderScaleUtilities.ResolveWorkingScale(
            [EffectiveScale.At(0.5f), EffectiveScale.At(2.0f), EffectiveScale.Unbounded],
            outputScale: 1.0f);
        Assert.That(w, Is.EqualTo(2.0f));
    }

    [Test]
    public void Resolve_MaxWorkingScale_CapsResult()
    {
        float w = RenderScaleUtilities.ResolveWorkingScale(
            [EffectiveScale.At(4.0f)],
            outputScale: 1.0f,
            maxWorkingScale: 2.0f);
        Assert.That(w, Is.EqualTo(2.0f));
    }

    // --- Mixed bitmap + vector must not drag vector down to bitmap's density ---

    // A 0.5 bitmap beside vector must not pull w to 0.5; vector can draw at s_out.
    [TestCase(0.5f, 1.0f, TestName = "Resolve_LowResBitmapWithVector_FloorsAtOutput")]
    [TestCase(2.0f, 2.0f, TestName = "Resolve_HighResBitmapWithVector_KeepsBitmapDensity")]
    [TestCase(1.0f, 1.0f, TestName = "Resolve_UnitBitmapWithVector_IsByteIdentityNeutral")]
    public void Resolve_BitmapWithVector_IsMaxOfBitmapAndOutput(float bitmapDensity, float expected)
    {
        float w = RenderScaleUtilities.ResolveWorkingScale(
            [EffectiveScale.At(bitmapDensity), EffectiveScale.Unbounded],
            outputScale: 1.0f);
        Assert.That(w, Is.EqualTo(expected));
    }

    // --- CHARACTERIZATION: "densest input forces the whole boundary" (pinning known behavior) ---

    [Test]
    public void Resolve_SmallHighDensitySiblingBesideLowDensity_RaisesWholeBoundary()
    {
        float wWithVector = RenderScaleUtilities.ResolveWorkingScale(
            [EffectiveScale.At(8f), EffectiveScale.At(1f), EffectiveScale.Unbounded],
            outputScale: 1f);
        Assert.That(wWithVector, Is.EqualTo(8f),
            "one small At(8) sibling lifts the whole boundary to w == 8");

        float wDenseBesideVector = RenderScaleUtilities.ResolveWorkingScale(
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
        float w = RenderScaleUtilities.ResolveWorkingScale(
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
        float w = BufferDimensionBudget.EngineCeiling.ClampWorkingScale(new Rect(0, 0, 1920, 1080), 2.0f);
        Assert.That(w, Is.EqualTo(2.0f));
    }

    [Test]
    public void ClampBudget_LargeCoordinateSpanDoesNotOverflowPixelWidth()
    {
        var bounds = new Rect(-2_000_000_000f, 0, 4_000_000_000f, 1);

        float clamped = BufferDimensionBudget.EngineCeiling.ClampWorkingScale(bounds, 1);
        PixelRect footprint = PixelRect.FromRect(bounds, clamped);

        Assert.Multiple(() =>
        {
            Assert.That(clamped, Is.GreaterThan(0).And.LessThan(1));
            Assert.That(footprint.Width, Is.GreaterThanOrEqualTo(0));
            Assert.That(footprint.Width, Is.LessThanOrEqualTo(BufferDimensionBudget.EngineCeiling.MaxDimension));
        });
    }

    [Test]
    public void PreserveTargetSupply_UsesTheFinerAnisotropicAxis()
    {
        using var target = RenderTarget.CreateNull(100, 100);
        using var canvas = new ImmediateCanvas(target, RenderIntent.Preview, 1, 1, new Size(100, 100));
        using (canvas.PushTransform(Matrix.CreateScale(4, 0.25f)))
        {
            Assert.That(DeviceGridAlignment.ResolveLocalDensity(canvas), Is.EqualTo(4));
        }
    }

    [Test]
    public void PreserveTargetSupply_UsesTheMaximumSingularValueUnderShear()
    {
        using var target = RenderTarget.CreateNull(100, 100);
        using var canvas = new ImmediateCanvas(target, RenderIntent.Preview, 1, 1, new Size(100, 100));
        using (canvas.PushTransform(new Matrix(1, 1, 0, 1, 0, 0)))
        {
            float expected = MathF.Sqrt((3 + MathF.Sqrt(5)) / 2);
            Assert.That(DeviceGridAlignment.ResolveLocalDensity(canvas), Is.EqualTo(expected).Within(1e-6f));
        }
    }

    [Test]
    public void PreserveTargetSupply_RejectsPerspectiveInsteadOfApproximatingIt()
    {
        using var target = RenderTarget.CreateNull(100, 100);
        using var canvas = new ImmediateCanvas(target, RenderIntent.Preview, 1, 1, new Size(100, 100));
        var perspective = new Matrix(
            1, 0, 0.01f,
            0, 1, 0,
            0, 0, 1);
        using (canvas.PushTransform(perspective))
        {
            Assert.That(
                () => DeviceGridAlignment.ResolveLocalDensity(canvas),
                Throws.TypeOf<NotSupportedException>().With.Message.Contains("perspective"));
        }
    }

    [TestCase(-8f, 6f)]
    [TestCase(8f, -6f)]
    [TestCase(-8f, -6f)]
    [TestCase(0f, 0f)]
    public void ClampBudget_NeverReturnsAnUnusableDensityForADegenerateRectangle(float width, float height)
    {
        var bounds = new Rect(0, 0, width, height);

        Assert.Multiple(() =>
        {
            Assert.That(
                BufferDimensionBudget.EngineCeiling.ClampWorkingScaleToExactFootprint(bounds, 2f),
                Is.GreaterThan(0f));
            Assert.That(
                BufferDimensionBudget.EngineCeiling.ClampWorkingScaleToRasterApron(bounds, 2f),
                Is.GreaterThan(0f));
        });
    }

    [Test]
    public void ClampBudget_NeverIncreasesScale_AndGuardsNonFinite()
    {
        Assert.That(BufferDimensionBudget.EngineCeiling.ClampWorkingScale(new Rect(0, 0, 100, 100), 3.0f),
            Is.EqualTo(3.0f));
        Assert.That(BufferDimensionBudget.EngineCeiling.ClampWorkingScale(new Rect(0, 0, 100, 100), float.NaN),
            Is.NaN);
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
        float w = RenderScaleUtilities.ResolveWorkingScale(vectorOnly, badScale);
        Assert.That(w, Is.EqualTo(1f));
    }

    [Test]
    public void ResolveWorkingScale_DegenerateOutputScale_DoesNotDragDownAConcreteSupply()
    {
        ReadOnlySpan<EffectiveScale> mixed = [EffectiveScale.At(2f), EffectiveScale.Unbounded];
        float w = RenderScaleUtilities.ResolveWorkingScale(mixed, float.NaN);
        Assert.That(w, Is.EqualTo(2f));
    }

    // --- s_out floor across supersample / preview outputs ---

    [TestCase(0.5f, 2.0f, 2.0f)] // sub-output proxy floored to 2.0
    [TestCase(0.5f, 1.0f, 1.0f)] // sub-output proxy floored to 1.0
    [TestCase(1.0f, 2.0f, 2.0f)] // 1:1 source floored to 2.0
    [TestCase(2.0f, 2.0f, 2.0f)] // supply matches output
    [TestCase(2.0f, 1.0f, 2.0f)] // supply wins, s_out not a ceiling
    [TestCase(0.5f, 0.5f, 0.5f)] // 0.5 proxy at 0.5 preview: floor inert
    public void Resolve_ConcreteSupply_IsMaxOfSupplyAndOutput(float supply, float outputScale, float expected)
    {
        float w = RenderScaleUtilities.ResolveWorkingScale([EffectiveScale.At(supply)], outputScale);
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

    // --- RenderNodeRenderer sanitizes degenerate request scale at the boundary ---

    [TestCase(0f)]
    [TestCase(-2f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void RenderNodeRenderer_SanitizesDegenerateOutputScaleToOne(float bad)
    {
        using var node = new ContainerRenderNode();
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            OutputScale = bad,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
        });
        Assert.That(renderer.DefaultRequest.OutputScale, Is.EqualTo(1f));
    }

    [TestCase(float.NaN)]
    [TestCase(0f)]
    [TestCase(-1f)]
    public void RenderNodeRenderer_DegenerateMaxWorkingScale_IsTreatedAsNoCeiling(float bad)
    {
        using var node = new ContainerRenderNode();
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            OutputScale = 1,
            MaxWorkingScale = bad,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
        });
        Assert.That(renderer.DefaultRequest.MaxWorkingScale, Is.EqualTo(float.PositiveInfinity));
        float w = RenderScaleUtilities.ResolveWorkingScale(
            [EffectiveScale.At(3f)],
            1f,
            renderer.DefaultRequest.MaxWorkingScale);
        Assert.That(w, Is.EqualTo(3f));
    }

    // --- Shader device-buffer dimensions (the size SKSL/GLSL resolution uniforms must report) ------------

    [Test]
    public void DeviceBufferSize_MatchesCreateTargetFormula()
    {
        // EffectItem custom effects size local buffers from dimensions, not from rounded global edges.
        Assert.That(CustomFilterEffectContext.DeviceBufferSize(new Rect(0, 0, 100.7f, 50.2f), 1f),
            Is.EqualTo((100, 50)), "the historical w == 1 path truncates fractional dimensions");
        Assert.That(CustomFilterEffectContext.DeviceBufferSize(new Rect(0, 0, 100.0f, 50.0f), 2f),
            Is.EqualTo((200, 100)), "integral bounds * w stays integral");
        Assert.That(CustomFilterEffectContext.DeviceBufferSize(new Rect(0, 0, 100.3f, 50.1f), 2f),
            Is.EqualTo((201, 101)), "fractional bounds * w ceils up");
        Assert.That(CustomFilterEffectContext.DeviceBufferSize(new Rect(10.25f, 20.25f, 8, 6), 2f),
            Is.EqualTo((16, 12)), "fractional origins do not affect a local buffer's dimensions");
    }

    // --- Pass-through input boundary: EffectiveScale must survive request-local facade remapping ----------

    [Test]
    public void PassThroughNode_ForwardsRecordedInputEffectiveScale()
    {
        RenderNodeMeasurement measurement = ScaleRecordingTestHelper.MeasureThrough(
            new PassThroughRenderNode(),
            EffectiveScale.At(0.5f));

        Assert.That(measurement.HasFragments, Is.True);
        Assert.That(measurement.EffectiveScale.IsUnbounded, Is.False,
            "the pass-through facade must not collapse a concrete supply density to Unbounded");
        Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(0.5f));
    }

    private sealed class PassThroughRenderNode : RenderNode
    {
        public override void Process(RenderNodeContext context)
            => context.PassThrough();
    }
}
