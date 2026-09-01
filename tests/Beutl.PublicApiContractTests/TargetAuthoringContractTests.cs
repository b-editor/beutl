using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class TargetAuthoringContractTests
{
    private static readonly Rect s_bounds = new(0, 0, 8, 6);
    private static readonly RenderResourceSlot<CommandPayload> s_payloadSlot = new();
    private static OpaqueRenderDescription SourceDescription(Color color)
        => OpaqueRenderDescription.Create(
            color,
            static (session, current) =>
            {
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                output.Canvas.Use(canvas => canvas.Clear(current));
                session.Publish(output);
            },
            OpaqueRenderBoundsContract.Source(s_bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale);

    private static TargetScopeDescription ReplayScope()
        => TargetScopeDescription.Create(
            (byte)0,
            static (session, _) => session.ReplayInput(),
            RenderBoundsContract.Identity,
            RenderHitTestContract.AnyInput,
            RenderScaleContract.PreserveInputSupply);

    private static TargetCommandDescription SlotReadingCommand(
        CommandState state,
        RenderResourceBinding payload)
        => TargetCommandDescription.Create(
            state,
            static (session, current) => session.UseResource(s_payloadSlot, payload =>
            {
                payload.Uses++;
                current.Executions++;
                session.ReplaceAffectedRegion(current.Color);
            }),
            TargetRegion.Region(s_bounds),
            s_bounds,
            RenderHitTestContract.OutputBounds,
            resources: [payload],
            slots: [s_payloadSlot]);

    private static TargetCommandDescription ShapeCommand(CommandState state)
        => TargetCommandDescription.Create(
            state,
            static (session, current) => session.ReplaceAffectedRegion(current.Color),
            TargetRegion.Region(s_bounds),
            s_bounds,
            RenderHitTestContract.OutputBounds);

    [Test]
    public void TargetDescriptions_RecordAsTargetEffectsRatherThanValueInputs()
    {
        var commandState = new CommandState(Colors.Red);
        bool scopeEligible = true;
        bool commandEligible = true;

        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(SourceDescription(Colors.CornflowerBlue));
            RenderFragmentHandle scope = context.TargetScope(source, ReplayScope());
            RenderFragmentHandle command = context.TargetCommand([], ShapeCommand(commandState));
            scopeEligible = scope.CanBeUsedAsValueInput;
            commandEligible = command.CanBeUsedAsValueInput;
            context.PublishRange([scope, command]);
        });

        RenderNodeMeasurement measurement = Measure(node);

        Assert.Multiple(() =>
        {
            Assert.That(scopeEligible, Is.False);
            Assert.That(commandEligible, Is.False);
            Assert.That(measurement.HasTargetEffects, Is.True);
            Assert.That(measurement.HasContributingValues, Is.True);
        });
    }

    [Test]
    public void ATargetCommand_UsesTheResourceBoundToItsDeclaredSlot()
    {
        var payload = new CommandPayload();
        var state = new CommandState(Colors.MediumPurple);
        using var node = new DelegateNode(context =>
        {
            RenderResource<CommandPayload> token = context.Borrow(payload);
            context.Publish(context.TargetCommand(
                [],
                SlotReadingCommand(state, s_payloadSlot.Bind(token))));
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
    public void ATargetCommand_RejectsAHitTestForAnEmptyQueryRegion()
    {
        Assert.That(
            () => TargetCommandDescription.Create(
                (byte)0,
                static (_, _) => { },
                TargetRegion.Region(s_bounds),
                Rect.Empty,
                RenderHitTestContract.OutputBounds),
            Throws.TypeOf<ArgumentException>().With.Property("ParamName").EqualTo("hitTest"));
    }

    [Test]
    public void ATargetCommand_DeclaresReadbackThroughItsDescription()
    {
        int snapshots = 0;
        using var node = new DelegateNode(context =>
            context.Publish(context.TargetCommand(
                [],
                RenderDescriptionFactory.TargetCommand(
                    session => session.UseSnapshot(_ => snapshots++),
                    TargetRegion.Region(s_bounds),
                    Rect.Empty,
                    RenderHitTestContract.None,
                    access: TargetAccess.Readback))));

        using RenderNodeRasterization rasterization = Rasterize(node);

        Assert.That(snapshots, Is.EqualTo(1));
    }

    [Test]
    public void ARawTargetCommand_RemainsAnExplicitlyDeclaredBoundary()
    {
        int executions = 0;
        using var node = new DelegateNode(context =>
            context.Publish(context.RawTargetCommand(
                RawTargetCommandDescription.Create<Action<RawTargetCommandSession>>(
                    _ => executions++,
                    static (session, action) => action(session),
                    Rect.Empty,
                    RenderHitTestContract.None))));

        using RenderNodeRasterization rasterization = Rasterize(node);

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(executions, Is.EqualTo(1));
        });
    }

    [Test]
    public void ANodeRecordedWithExplicitInputs_IsStillPreparedForTheRequest()
    {
        var recorded = new PreparationCountingNode();
        using var node = new DelegateNode(context =>
        {
            RenderFragmentHandle source = context.OpaqueSource(SourceDescription(Colors.White));
            foreach (RenderFragmentHandle output in context.RecordNode(recorded, [source]))
                context.Publish(output);
        });

        using RenderNodeRasterization first = Rasterize(node);
        using RenderNodeRasterization second = Rasterize(node);

        Assert.Multiple(() =>
        {
            Assert.That(recorded.Preparations, Is.EqualTo(2), "one preparation per request");
            Assert.That(
                recorded.Processes,
                Is.EqualTo(1),
                "the second request's inputs digest to what the recording was made over, so it is served");
            Assert.That(
                recorded.Processes,
                Is.LessThanOrEqualTo(recorded.Preparations),
                "no Process may run for a request that did not prepare the node first");
        });
    }

    [Test]
    public void AGuardedScopeDeclaresTheSpaceItsReplayTransformLivesIn()
    {
        TargetScopeDescription inputLogical = TargetScopeDescription.Create(
            (byte)0,
            static (session, _) => session.Canvas.Use(_ => session.ReplayInput()),
            RenderBoundsContract.Identity,
            RenderHitTestContract.AnyInput,
            RenderScaleContract.MapInputSupply(
                static supply => supply,
                static demand => EffectiveScale.At(demand.Value * 2)),
            transformSpace: RenderScopeTransformSpace.InputLogical);
        using var node = new DelegateNode(context => context.Publish(context.TargetScope(
            context.OpaqueSource(SourceDescription(Colors.White)),
            inputLogical)));

        using RenderNodeRasterization rasterization = Rasterize(node);

        Assert.That(rasterization.IsEmpty, Is.False);
    }

    [Test]
    public void ARawCommandAddressesItsResourceByTheSlotItDeclared()
    {
        var payload = new CommandPayload();
        using var node = new DelegateNode(context => context.Publish(context.RawTargetCommand(
            RawTargetCommandDescription.Create(
                (byte)0,
                static (session, _) => session.UseResource(s_payloadSlot, static bound => bound.Uses++),
                Rect.Empty,
                RenderHitTestContract.None,
                resources: [s_payloadSlot.Bind(context.Borrow(payload))],
                slots: [s_payloadSlot]))));

        using RenderNodeRasterization rasterization = Rasterize(node);

        Assert.That(payload.Uses, Is.EqualTo(1));
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
                    Intent = RenderIntent.Preview,
                    TargetDomain = s_bounds,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });

    private sealed class DelegateNode(Action<RenderNodeContext> process) : RenderNode
    {
        public override void Process(RenderNodeContext context) => process(context);
    }

    private sealed class PreparationCountingNode : RenderNode
    {
        public int Preparations { get; private set; }

        public int Processes { get; private set; }

        public override void PrepareForRequest(RenderNodePreparation preparation) => Preparations++;

        public override void Process(RenderNodeContext context)
        {
            Processes++;
            context.PassThrough();
        }
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
