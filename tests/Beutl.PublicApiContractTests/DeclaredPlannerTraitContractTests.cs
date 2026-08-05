using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

/// <summary>
/// Covers the two planner traits an out-of-tree render node may declare: an opaque description's dependence
/// on the device-grid phase, and a target scope's device-grid mapping. Value-replay-map eligibility is
/// deliberately absent — it stays engine-owned, so a public scope is always a materializing boundary.
/// </summary>
[TestFixture]
public sealed class DeclaredPlannerTraitContractTests
{
    private const string IdentityCurrentPixel = "half4 apply(half4 color) { return color; }";

    private static readonly Rect s_bounds = new(0, 0, 16, 12);

    private static readonly Matrix s_subpixelShift = Matrix.CreateTranslation(3.25f, 4.5f);

    [TestCase(RenderDeviceGridSensitivity.Insensitive, 1)]
    [TestCase(RenderDeviceGridSensitivity.PhaseDependent, 2)]
    public void DeclaredDeviceGridSensitivity_DecidesCacheReuseUnderARemappingScope(
        RenderDeviceGridSensitivity sensitivity,
        int expectedExecutions)
    {
        using var producer = new DeclaringSourceNode(sensitivity);
        producer.Cache.ReportRenderCount(RenderNodeCache.Count);
        using var root = new ScopeNode(
            producer,
            RenderDeviceGridMapping.Remapped,
            s_subpixelShift);
        using RenderNodeRenderer renderer = CreateFrameRenderer(root);

        using (RenderNodeRasterization first = renderer.Rasterize())
        {
            Assert.That(first.IsEmpty, Is.False);
        }

        using RenderNodeRasterization second = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(second.IsEmpty, Is.False);
            Assert.That(
                producer.ExecuteCount,
                Is.EqualTo(expectedExecutions),
                "A source that declares PhaseDependent must be re-executed under a remapping scope, "
                + "while an insensitive source is served from its cached output.");
        });
    }

    [Test]
    public void APhaseDependentSource_UnderAGridPreservingScope_IsServedFromItsCachedOutput()
    {
        using var producer = new DeclaringSourceNode(RenderDeviceGridSensitivity.PhaseDependent);
        producer.Cache.ReportRenderCount(RenderNodeCache.Count);
        using var root = new ScopeNode(
            producer,
            RenderDeviceGridMapping.Preserved,
            transform: null);
        using RenderNodeRenderer renderer = CreateFrameRenderer(root);

        using (RenderNodeRasterization first = renderer.Rasterize())
        {
        }

        using RenderNodeRasterization second = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(second.IsEmpty, Is.False);
            Assert.That(
                producer.ExecuteCount,
                Is.EqualTo(1),
                "A grid-preserving scope keeps the phase the cached output was captured at.");
        });
    }

    [TestCase(RenderDeviceGridMapping.Remapped)]
    [TestCase(RenderDeviceGridMapping.Preserved)]
    public void APublicScope_IsNeverValueInputEligible_WhicheverGridMappingItDeclares(
        RenderDeviceGridMapping mapping)
    {
        bool scopeIsValueEligible = true;
        ArgumentException? rejection = null;
        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(ExecutingSource(
                RenderDeviceGridSensitivity.Insensitive,
                $"public-scope-source-{mapping}"));
            RenderFragmentHandle scope = context.TargetScope(
                source,
                ScopeDescription(mapping, s_subpixelShift, $"public-scope-{mapping}"));
            scopeIsValueEligible = scope.CanBeUsedAsValueInput;
            try
            {
                context.Shader(scope, ShaderDescription.CurrentPixel(IdentityCurrentPixel));
            }
            catch (ArgumentException ex)
            {
                rejection = ex;
            }

            context.Publish(scope);
        });

        using RenderNodeRasterization rasterization = Rasterize(node);

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(scopeIsValueEligible, Is.False,
                "Value-replay-map eligibility is engine-owned and cannot be declared by a public scope.");
            Assert.That(rejection, Is.Not.Null);
        });
    }

    [Test]
    public void UnstatedTraits_SelectTheDocumentedDefaults()
    {
        OpaqueRenderDescription opaque = OpaqueRenderDescription.Create(
            static _ => { },
            OpaqueRenderBoundsContract.Source(s_bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale,
            structuralKey: "unstated-opaque");
        TargetScopeDescription scope = TargetScopeDescription.Create(
            static session => session.Canvas.Use(_ => session.ReplayInput()),
            RenderBoundsContract.Identity,
            RenderHitTestContract.AnyInput,
            RenderScaleContract.PreserveInputSupply,
            structuralKey: "unstated-scope");

        Assert.Multiple(() =>
        {
            Assert.That(
                opaque.DeviceGridSensitivity,
                Is.EqualTo(RenderDeviceGridSensitivity.Insensitive));
            Assert.That(scope.DeviceGridMapping, Is.EqualTo(RenderDeviceGridMapping.Remapped));
        });
    }

    [Test]
    public void OpaqueRenderDescriptionCreate_RejectsAnUndefinedDeviceGridSensitivity()
    {
        ArgumentOutOfRangeException? exception = Assert.Throws<ArgumentOutOfRangeException>(
            static () => OpaqueRenderDescription.Create(
                static _ => { },
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                (RenderDeviceGridSensitivity)7,
                structuralKey: "undefined-sensitivity"));

        Assert.That(exception!.ParamName, Is.EqualTo("deviceGridSensitivity"));
    }

    [Test]
    public void TargetScopeDescriptionCreate_RejectsAnUndefinedDeviceGridMapping()
    {
        ArgumentOutOfRangeException? exception = Assert.Throws<ArgumentOutOfRangeException>(
            static () => TargetScopeDescription.Create(
                static session => session.Canvas.Use(_ => session.ReplayInput()),
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput,
                RenderScaleContract.PreserveInputSupply,
                (RenderDeviceGridMapping)7,
                structuralKey: "undefined-mapping"));

        Assert.That(exception!.ParamName, Is.EqualTo("deviceGridMapping"));
    }

    private static OpaqueRenderDescription ExecutingSource(
        RenderDeviceGridSensitivity sensitivity,
        object structuralKey,
        Action? beforePublish = null)
    {
        return OpaqueRenderDescription.Create(
            session =>
            {
                beforePublish?.Invoke();
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                output.Canvas.Use(static canvas => canvas.Clear(Colors.CornflowerBlue));
                session.Publish(output);
            },
            OpaqueRenderBoundsContract.Source(s_bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale,
            deviceGridSensitivity: sensitivity,
            structuralKey: structuralKey,
            runtimeIdentity: new RenderRuntimeIdentity(("source-runtime", structuralKey)));
    }

    private static TargetScopeDescription ScopeDescription(
        RenderDeviceGridMapping mapping,
        Matrix? transform,
        object structuralKey)
    {
        var boundsState = new TransformBoundsState(transform ?? Matrix.Identity);
        return TargetScopeDescription.Create(
            session => session.Canvas.Use(canvas =>
            {
                if (transform is not { } matrix)
                {
                    session.ReplayInput();
                    return;
                }

                using (canvas.PushTransform(matrix))
                {
                    session.ReplayInput();
                }
            }),
            RenderBoundsContract.Create(
                boundsState.Forward,
                boundsState.Backward,
                structuralKey: ("scope-bounds", structuralKey)),
            RenderHitTestContract.AnyInput,
            RenderScaleContract.PreserveInputSupply,
            mapping,
            structuralKey: structuralKey,
            runtimeIdentity: new RenderRuntimeIdentity(("scope-runtime", structuralKey)));
    }

    private static RenderNodeRenderer CreateFrameRenderer(RenderNode node)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_bounds,
                    CacheOptions = RenderCacheOptions.Enabled,
                    Purpose = RenderRequestPurpose.Frame,
                },
            });

    private static RenderNodeRasterization Rasterize(RenderNode node)
    {
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_bounds,
                    CacheOptions = RenderCacheOptions.Disabled,
                },
            });
        return renderer.Rasterize();
    }

    private readonly record struct TransformBoundsState(Matrix Transform)
    {
        public Rect Forward(Rect value) => value.TransformToAABB(Transform);

        public Rect Backward(Rect value)
            => Transform.HasInverse ? value.TransformToAABB(Transform.Invert()) : value;
    }

    private sealed class DeclaringSourceNode(RenderDeviceGridSensitivity sensitivity) : RenderNode
    {
        public int ExecuteCount { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.OpaqueSource(ExecutingSource(
                sensitivity,
                typeof(DeclaringSourceNode),
                () => ExecuteCount++)));
        }
    }

    private sealed class ScopeNode(
        RenderNode producer,
        RenderDeviceGridMapping mapping,
        Matrix? transform) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle input = context.RecordNode(producer, []).Single();
            context.Publish(context.TargetScope(
                input,
                ScopeDescription(mapping, transform, typeof(ScopeNode))));
        }
    }

    private sealed class DelegateNode(Action<RenderNodeContext> process) : RenderNode
    {
        public override void Process(RenderNodeContext context) => process(context);
    }
}
