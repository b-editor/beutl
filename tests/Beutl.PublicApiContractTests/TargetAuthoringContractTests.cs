using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class TargetAuthoringContractTests
{
    private static readonly Rect s_bounds = new(0, 0, 8, 6);
    private static readonly RenderResourceSlot<CommandPayload> s_payloadSlot = new();
    private static readonly OpaqueRenderDefinition<Color> s_sourceDefinition =
        OpaqueRenderDefinition<Color>.Create(
            static (session, color) =>
            {
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                output.Canvas.Use(canvas => canvas.Clear(color));
                session.Publish(output);
            },
            OpaqueRenderBoundsContract.Source(s_bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale);
    private static readonly TargetScopeDefinition<byte> s_scopeDefinition =
        TargetScopeDefinition<byte>.Create(
            static (session, _) => session.ReplayInput(),
            RenderBoundsContract.Identity,
            RenderHitTestContract.AnyInput,
            RenderScaleContract.PreserveInputSupply,
            resources: []);
    private static readonly TargetCommandDefinition<CommandState> s_commandDefinition =
        TargetCommandDefinition<CommandState>.Create(
            static (session, state) => session.UseResource(s_payloadSlot, payload =>
            {
                payload.Uses++;
                state.Executions++;
                session.ReplaceAffectedRegion(state.Color);
            }),
            TargetRegion.Region(s_bounds),
            s_bounds,
            RenderHitTestContract.OutputBounds,
            resources: [s_payloadSlot]);
    private static readonly TargetCommandDefinition<CommandState> s_shapeCommandDefinition =
        TargetCommandDefinition<CommandState>.Create(
            static (session, state) => session.ReplaceAffectedRegion(state.Color),
            TargetRegion.Region(s_bounds),
            s_bounds,
            RenderHitTestContract.OutputBounds);

    [Test]
    public void TargetDefinitionCalls_RecordTheFixedDefinitionShapeAndPerCallState()
    {
        var commandState = new CommandState(Colors.Red);
        OpaqueRenderCall<Color> sourceCall = s_sourceDefinition.Call(Colors.CornflowerBlue);
        TargetScopeCall<byte> scopeCall = s_scopeDefinition.Call(default);
        TargetCommandCall<CommandState> commandCall = s_shapeCommandDefinition.Call(commandState);
        bool scopeEligible = true;
        bool commandEligible = true;

        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(sourceCall);
            RenderFragmentHandle scope = context.TargetScope(source, scopeCall);
            RenderFragmentHandle command = context.TargetCommand([], commandCall);
            scopeEligible = scope.CanBeUsedAsValueInput;
            commandEligible = command.CanBeUsedAsValueInput;
            context.PublishRange([scope, command]);
        });

        RenderNodeMeasurement measurement = Measure(node);

        Assert.Multiple(() =>
        {
            Assert.That(sourceCall.Definition, Is.SameAs(s_sourceDefinition));
            Assert.That(sourceCall.State, Is.EqualTo(Colors.CornflowerBlue));
            Assert.That(scopeCall.Definition, Is.SameAs(s_scopeDefinition));
            Assert.That(commandCall.Definition, Is.SameAs(s_shapeCommandDefinition));
            Assert.That(commandCall.State, Is.SameAs(commandState));
            Assert.That(scopeEligible, Is.False);
            Assert.That(commandEligible, Is.False);
            Assert.That(measurement.HasTargetEffects, Is.True);
            Assert.That(measurement.HasContributingValues, Is.True);
        });
    }

    [Test]
    public void TargetCommandCall_UsesTheResourceBoundToItsDeclaredSlot()
    {
        var payload = new CommandPayload();
        var state = new CommandState(Colors.MediumPurple);
        using var node = new DelegateNode(context =>
        {
            RenderResource<CommandPayload> token = context.Borrow(payload);
            context.Publish(context.TargetCommand(
                [],
                s_commandDefinition.Call(state, [s_payloadSlot.Bind(token)])));
        });

        using RenderNodeRasterization rasterization = Rasterize(node);

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(payload.Uses, Is.EqualTo(1));
            Assert.That(state.Executions, Is.EqualTo(1));
        });
    }

    [Test]
    public void TargetCommandCall_RejectsAHitTestForAnEmptyQueryRegion()
    {
        Assert.That(
            () => TargetCommandDefinition<byte>.Create(
                static (_, _) => { },
                TargetRegion.Region(s_bounds),
                Rect.Empty,
                RenderHitTestContract.OutputBounds),
            Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("hitTest"));
    }

    [Test]
    public void TargetCommandCall_DeclaresReadbackThroughTheDefinition()
    {
        int snapshots = 0;
        TargetCommandDefinition<Action<TargetCommandSession>> definition =
            TargetCommandDefinition<Action<TargetCommandSession>>.Create(
                static (session, action) => action(session),
                TargetRegion.Region(s_bounds),
                Rect.Empty,
                RenderHitTestContract.None,
                access: TargetAccess.Readback);
        using var node = new DelegateNode(context =>
            context.Publish(context.TargetCommand(
                [],
                definition.Call(session => session.UseSnapshot(_ => snapshots++)))));

        using RenderNodeRasterization rasterization = Rasterize(node);

        Assert.That(snapshots, Is.EqualTo(1));
    }

    [Test]
    public void RawTargetCommandCall_RemainsAnExplicitDefinitionBasedBoundary()
    {
        int executions = 0;
        RawTargetCommandDefinition<Action<RawTargetCommandSession>> definition =
            RawTargetCommandDefinition<Action<RawTargetCommandSession>>.Create(
                static (session, action) => action(session),
                Rect.Empty,
                RenderHitTestContract.None);
        using var node = new DelegateNode(context =>
            context.Publish(context.RawTargetCommand(definition.Call(_ => executions++))));

        using RenderNodeRasterization rasterization = Rasterize(node);

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(executions, Is.EqualTo(1));
        });
    }

    private static RenderNodeMeasurement Measure(RenderNode node)
    {
        using var renderer = CreateRenderer(node);
        return renderer.Measure();
    }

    private static RenderNodeRasterization Rasterize(RenderNode node)
    {
        using var renderer = CreateRenderer(node);
        return renderer.Rasterize();
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode node)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = s_bounds,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });

    private sealed class DelegateNode(Action<RenderNodeContext> process) : RenderNode
    {
        public override void Process(RenderNodeContext context) => process(context);
    }

    private sealed class CommandPayload
    {
        public int Uses { get; set; }
    }

    private sealed class CommandState(Color color)
    {
        public Color Color { get; } = color;

        public int Executions { get; set; }
    }
}
