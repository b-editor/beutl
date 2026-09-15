using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class RenderNodeContextMetadataContractTests
{
    private static readonly Rect s_bounds = new(3, 5, 12, 8);

    [Test]
    public void RecordingMetadataQueries_DistinguishValueBoundsFromSymbolicTargetWrites()
    {
        using var valueOnly = new PluginInputProbeNode();
        valueOnly.AddChild(new SolidSourceNode(s_bounds, Colors.CornflowerBlue));
        using var symbolicWrite = new PluginInputProbeNode();
        symbolicWrite.AddChild(new RawTargetWriteNode());

        Measure(valueOnly);
        Measure(symbolicWrite);

        Assert.Multiple(() =>
        {
            Assert.That(valueOnly.FragmentHint, Is.EqualTo(s_bounds));
            Assert.That(valueOnly.InputHint, Is.EqualTo(s_bounds));
            Assert.That(valueOnly.HasSymbolicTargetWrite, Is.False);
            Assert.That(valueOnly.HasFiniteIsolationDomain, Is.True);
            Assert.That(valueOnly.IsolationDomain, Is.EqualTo(s_bounds));
            Assert.That(symbolicWrite.FragmentHint, Is.EqualTo(Rect.Empty));
            Assert.That(symbolicWrite.InputHint, Is.EqualTo(Rect.Empty));
            Assert.That(symbolicWrite.HasSymbolicTargetWrite, Is.True);
            Assert.That(symbolicWrite.HasFiniteIsolationDomain, Is.False);
        });
    }

    // A plugin node whose value exists only on the 3D backend needs to know, while it records, whether the
    // request can produce it - and it must not ask the process itself. The context carries that answer as
    // request state, so like the rest of the request it answers while the node records and not after.
    [Test]
    public void RecordingContext_ReportsBackendCapabilityOnlyWhileTheNodeRecords()
    {
        using var probe = new BackendCapabilityProbeNode();

        Measure(probe);

        Assert.Multiple(() =>
        {
            Assert.That(probe.Supports3DRendering, Is.Not.Null, "the node must read the capability while recording");
            Assert.That(probe.RetainedContext, Is.Not.Null);
            Assert.That(
                () => _ = probe.RetainedContext!.Supports3DRendering,
                Throws.TypeOf<InvalidOperationException>(),
                "a context kept past its transaction must not keep answering for a request that has ended");
        });
    }

    private static void Measure(RenderNode node)
    {
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                TargetDomain = new Rect(0, 0, 64, 64),
            });
        renderer.Measure();
    }

    private sealed class PluginInputProbeNode : ContainerRenderNode
    {
        public Rect FragmentHint { get; private set; }

        public Rect InputHint { get; private set; }

        public bool HasSymbolicTargetWrite { get; private set; }

        public bool HasFiniteIsolationDomain { get; private set; }

        public Rect IsolationDomain { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            Rect fragmentHint = default;
            foreach (RenderFragmentHandle input in context.Inputs)
                fragmentHint = fragmentHint.Union(context.GetRecordedMetadataHint(input).Bounds);

            FragmentHint = fragmentHint;
            InputHint = context.CalculateRecordedInputBoundsHint();
            HasSymbolicTargetWrite = context.HasSymbolicInputTargetWrite();
            HasFiniteIsolationDomain = context.TryCalculateFiniteIsolationDomain(out Rect domain);
            IsolationDomain = domain;
            context.PassThrough();
        }
    }

    private sealed class BackendCapabilityProbeNode : RenderNode
    {
        public bool? Supports3DRendering { get; private set; }

        public RenderNodeContext? RetainedContext { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            Supports3DRendering = context.Supports3DRendering;
            RetainedContext = context;
            context.PassThrough();
        }
    }

    private sealed class RawTargetWriteNode : RenderNode
    {
        public override void Process(RenderNodeContext context)
            => context.Publish(context.RawTargetCommand(RawTargetCommandDescription.Create(
                (byte)0,
                static (_, _) => { },
                Rect.Empty,
                RenderHitTestContract.None)));
    }

    private sealed class SolidSourceNode(Rect bounds, Color color) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                (bounds, color),
                static (session, state) =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(canvas => canvas.Clear(state.color));
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Source(bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale);
            context.Publish(context.OpaqueSource(description));
        }
    }
}
