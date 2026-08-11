using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class DeclaredPlannerTraitContractTests
{
    private static readonly Rect s_bounds = new(0, 0, 16, 12);

    [Test]
    public void Definitions_ValidateTheirDeclaredDeviceGridTraits()
    {
        ArgumentOutOfRangeException? opaque = Assert.Throws<ArgumentOutOfRangeException>(
            static () => OpaqueRenderDefinition<byte>.Create(
                static (_, _) => { },
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                deviceGridSensitivity: (RenderDeviceGridSensitivity)7));
        ArgumentOutOfRangeException? scopeMapping = Assert.Throws<ArgumentOutOfRangeException>(
            static () => TargetScopeDefinition<byte>.Create(
                static (_, _) => { },
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput,
                RenderScaleContract.PreserveInputSupply,
                deviceGridMapping: (RenderDeviceGridMapping)7));
        ArgumentOutOfRangeException? scopeSensitivity = Assert.Throws<ArgumentOutOfRangeException>(
            static () => TargetScopeDefinition<byte>.Create(
                static (_, _) => { },
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput,
                RenderScaleContract.PreserveInputSupply,
                deviceGridSensitivity: (RenderDeviceGridSensitivity)7));

        Assert.Multiple(() =>
        {
            Assert.That(opaque!.ParamName, Is.EqualTo("deviceGridSensitivity"));
            Assert.That(scopeMapping!.ParamName, Is.EqualTo("deviceGridMapping"));
            Assert.That(scopeSensitivity!.ParamName, Is.EqualTo("deviceGridSensitivity"));
        });
    }

    [Test]
    public void PublicTargetScopeCall_RemainsAnEffectBoundary()
    {
        bool valueEligible = true;
        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(SourceCall(Colors.CornflowerBlue));
            RenderFragmentHandle scope = context.TargetScope(
                source,
                RenderDefinitionCallFactory.TargetScope(
                    static session => session.ReplayInput(),
                    RenderBoundsContract.Identity,
                    RenderHitTestContract.AnyInput,
                    RenderScaleContract.PreserveInputSupply,
                    deviceGridSensitivity: RenderDeviceGridSensitivity.Insensitive,
                    deviceGridMapping: RenderDeviceGridMapping.Preserved));
            valueEligible = scope.CanBeUsedAsValueInput;
            context.Publish(scope);
        });

        RenderNodeMeasurement measurement = Measure(node);

        Assert.Multiple(() =>
        {
            Assert.That(valueEligible, Is.False);
            Assert.That(measurement.HasTargetEffects, Is.True);
            Assert.That(measurement.HasContributingValues, Is.True);
        });
    }

    [Test]
    public void CallStateChange_IsInvalidatedThroughHasChangesWithoutManualCacheControl()
    {
        using var node = new StatefulSourceNode(Colors.Red);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_bounds,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Enabled,
                    Purpose = RenderRequestPurpose.Frame,
                },
            });

        for (int i = 0; i < 5; i++)
        {
            using RenderNodeRasterization _ = renderer.Rasterize();
        }

        int beforeChange = node.ExecutionCount;
        node.Color = Colors.Blue;

        using (RenderNodeRasterization stale = renderer.Rasterize())
        {
            Assert.That(stale.IsEmpty, Is.False);
        }

        Assert.Multiple(() =>
        {
            Assert.That(node.ExecutionCount, Is.EqualTo(beforeChange));
            Assert.That(node.ExecutedColors.Last(), Is.EqualTo(Colors.Red));
        });

        node.HasChanges = true;

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(node.ExecutionCount, Is.GreaterThan(beforeChange));
            Assert.That(node.ExecutedColors.Last(), Is.EqualTo(Colors.Blue));
            Assert.That(node.HasChanges, Is.False);
        });
    }

    private static OpaqueRenderCall<Color> SourceCall(Color color)
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
            RenderScaleContract.MaterializeAtWorkingScale,
            RenderDeviceGridSensitivity.Insensitive)
            .Call(color);

    private static RenderNodeMeasurement Measure(RenderNode node)
    {
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest { TargetDomain = s_bounds },
            });
        return renderer.Measure();
    }

    private sealed class StatefulSourceNode(Color initialColor) : RenderNode
    {
        private static readonly OpaqueRenderDefinition<StatefulSourceNode> s_definition =
            OpaqueRenderDefinition<StatefulSourceNode>.Create(
                static (session, node) => node.Execute(session),
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                RenderDeviceGridSensitivity.Insensitive);

        public Color Color { get; set; } = initialColor;

        public int ExecutionCount { get; private set; }

        public List<Color> ExecutedColors { get; } = [];

        public override void Process(RenderNodeContext context)
            => context.Publish(context.OpaqueSource(s_definition.Call(this)));

        private void Execute(OpaqueRenderSession session)
        {
            ExecutionCount++;
            ExecutedColors.Add(Color);
            using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
            output.Canvas.Use(canvas => canvas.Clear(Color));
            session.Publish(output);
        }
    }

    private sealed class DelegateNode(Action<RenderNodeContext> process) : RenderNode
    {
        public override void Process(RenderNodeContext context) => process(context);
    }
}
