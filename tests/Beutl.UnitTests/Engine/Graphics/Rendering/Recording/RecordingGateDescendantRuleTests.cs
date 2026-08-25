using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

/// <summary>
/// Pins why the recording gate asks whether every recording below a node repeated, and not only whether the
/// node's input fingerprints match.
/// </summary>
/// <remarks>
/// <para>
/// <c>RenderFragmentReference.RecordingFingerprint</c> digests the recording metadata a consumer reads
/// through <see cref="RenderFragmentHandle"/> - bounds, effective scale, cardinality, the recording flags,
/// the payload's type - but not the fragment's hit test. <see cref="RenderFragmentHandle.TryHitTest"/> is
/// public and every built-in combinator that wraps an input (<c>ContributeValues</c>, <c>Opacity</c>,
/// <c>Blend</c>, <c>OpacityMask</c>) embeds that input's hit test into the fragment it records, and replay
/// clones a recorded fragment's hit test by reference. A node served over a fingerprint match therefore
/// keeps a hit test bound to the fragment it was recorded over, not the one it is replayed over.
/// </para>
/// <para>
/// So the descendant rule is load-bearing: it is the only thing that re-records an ancestor when a
/// descendant changes a hit test while leaving every fingerprinted field alone. Widening the gate to the
/// fingerprint alone turns these tests red, and closing the gap needs the fingerprint itself to cover the
/// hit-test contract.
/// </para>
/// </remarks>
[NonParallelizable]
[TestFixture]
public sealed class RecordingGateDescendantRuleTests
{
    private static readonly Rect s_bounds = new(0, 0, 100, 100);
    private static readonly Point s_inside = new(50, 50);

    /// <summary>The gap: the digest the gate compares cannot see a hit test at all.</summary>
    [Test]
    public void TwoFragmentsDifferingOnlyInTheirHitTest_ShareARecordingFingerprint()
    {
        using var hits = new HitTestSwappingNode(RenderHitTestContract.OutputBounds);
        using var misses = new HitTestSwappingNode(RenderHitTestContract.None);

        RenderFragmentReference hitting = OnlyFragmentOf(Record(hits));
        RenderFragmentReference missing = OnlyFragmentOf(Record(misses));

        Assert.Multiple(() =>
        {
            Assert.That(hitting.HitTest(s_inside), Is.True);
            Assert.That(missing.HitTest(s_inside), Is.False);
            Assert.That(
                missing.RecordingFingerprint,
                Is.EqualTo(hitting.RecordingFingerprint),
                "the fingerprint covers no hit test, so it cannot decide reuse for a node that embeds one");
        });
    }

    /// <summary>The guard, over a walked child: a hit-test-only change still re-records the ancestor.</summary>
    [Test]
    public void AChildThatChangesOnlyItsHitTest_StillForcesItsAncestorToRecordAgain()
    {
        using var child = new HitTestSwappingNode(RenderHitTestContract.OutputBounds);
        using var ancestor = new EmbedsInputHitTestNode();
        ancestor.AddChild(child);

        Record(ancestor);
        Record(ancestor);
        Assert.That(ancestor.ProcessCalls, Is.EqualTo(1), "an unchanged subtree must be served");

        child.HasChanges = true;
        child.Contract = RenderHitTestContract.None;
        RenderFragmentReference embedded = OnlyFragmentOf(Record(ancestor), RenderFragmentKind.Opacity);

        Assert.Multiple(() =>
        {
            Assert.That(
                ancestor.ProcessCalls,
                Is.EqualTo(2),
                "the ancestor embeds the child's hit test, which no input fingerprint reports");
            Assert.That(
                embedded.HitTest(s_inside),
                Is.False,
                "a served ancestor would answer with the hit test the child no longer has");
        });
    }

    /// <summary>The same guard over the explicit-input path, which is not walked.</summary>
    /// <remarks>
    /// A node reached with explicit inputs is ineligible today because the recorder passes
    /// <c>inputs.Count == 0</c> as its repeat flag. Deriving that flag from the input fingerprints instead
    /// would serve this wrapper, and it would answer with the hit test its input no longer has.
    /// </remarks>
    [Test]
    public void ANodeRecordedWithExplicitInputs_FollowsAHitTestOnlyChangeInThoseInputs()
    {
        using var source = new HitTestSwappingNode(RenderHitTestContract.OutputBounds);
        using var wrapper = new EmbedsInputHitTestNode();
        using var driver = new DrivesAWrapperOverAnotherNode(source, wrapper);

        Record(driver);
        Record(driver);

        source.HasChanges = true;
        source.Contract = RenderHitTestContract.None;
        RenderFragmentReference embedded = OnlyFragmentOf(Record(driver), RenderFragmentKind.Opacity);

        Assert.Multiple(() =>
        {
            Assert.That(
                embedded.HitTest(s_inside),
                Is.False,
                "a node served over matching input fingerprints keeps the hit test it was recorded over");
            Assert.That(
                wrapper.ProcessCalls,
                Is.EqualTo(3),
                "the explicit-input path re-records every request, which is what the fingerprint cannot widen");
        });
    }

    /// <summary>
    /// The cross-check accepts the very case the descendant rule rejects, so it cannot stand in for it.
    /// </summary>
    /// <remarks>
    /// <see cref="RecordedNodeShape"/> compares each payload's <c>StructuralFragmentIdentity</c>, and an
    /// opacity payload's identity carries its fusion description and opacity range - not the hit test it
    /// borrowed from its input. A widening this accepts is therefore not evidence that the widening is safe.
    /// </remarks>
    [Test]
    public void TheCrossCheck_AcceptsAnAncestorWhoseEmbeddedHitTestWouldGoStale()
    {
        if (!RenderRecordingCrossCheck.IsAvailable)
            Assert.Ignore("The cross-check call sites are compiled out of a Release build of Beutl.Engine.");

        using var child = new HitTestSwappingNode(RenderHitTestContract.OutputBounds);
        using var ancestor = new EmbedsInputHitTestNode();
        ancestor.AddChild(child);

        using (RenderRecordingCrossCheck.Enable())
        {
            Record(ancestor);
            child.HasChanges = true;
            child.Contract = RenderHitTestContract.None;

            Assert.That(() => Record(ancestor), Throws.Nothing);
        }
    }

    private static RenderFragmentReference OnlyFragmentOf(
        RecordedRenderGraph graph,
        RenderFragmentKind? kind = null)
    {
        foreach (RecordedRenderFragment fragment in graph.Fragments)
        {
            var reference = (RenderFragmentReference)fragment.Payload!;
            if (kind is null || reference.Kind == kind)
                return reference;
        }

        throw new InvalidOperationException($"The recorded graph has no {kind?.ToString() ?? "fragment"}.");
    }

    private static RecordedRenderGraph Record(RenderNode node)
    {
        RenderNodeCacheLifecycle lifecycle = RenderNodeCacheHelper.BeginLifecycle(node, cacheEnabled: false);
        using var owner = new RenderRequestOwner();
        using var request = new RenderRequest(
            new RenderNodeRecordingCacheTests.RequestSetup().CreateOptions(owner));
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        lifecycle.CompleteSuccessfully(false);
        return graph;
    }

    /// <summary>Records one bounds with a swappable hit-test contract, so only the hit test moves.</summary>
    private sealed class HitTestSwappingNode(RenderHitTestContract contract) : RenderNode
    {
        public RenderHitTestContract Contract { get; set; } = contract;

        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.OpaqueSource(OpaqueRenderDescription.CreateEngineSource(
                state: s_bounds,
                execute: static (session, state) =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(state);
                    output.Canvas.Use(static canvas => canvas.Clear());
                    session.Publish(output);
                },
                directReplay: static (session, _) => session.Canvas.Clear(),
                bounds: OpaqueRenderBoundsContract.Source(s_bounds),
                hitTest: Contract,
                scale: RenderScaleContract.Vector,
                deviceGridSensitivity: RenderDeviceGridSensitivity.Insensitive)));
        }
    }

    /// <summary>Wraps every input the way the built-in combinators do, borrowing the input's hit test.</summary>
    private sealed class EmbedsInputHitTestNode : ContainerRenderNode
    {
        public int ProcessCalls { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            ProcessCalls++;
            foreach (RenderFragmentHandle input in context.Inputs)
                context.Publish(context.Opacity(input, 0.5f));
        }
    }

    /// <summary>Records one node, then records a second over the fragments the first produced.</summary>
    private sealed class DrivesAWrapperOverAnotherNode(RenderNode inner, RenderNode wrapper) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            IReadOnlyList<RenderFragmentHandle> produced = context.RecordNode(inner, []);
            context.PublishRange(context.RecordNode(wrapper, produced));
        }
    }
}
