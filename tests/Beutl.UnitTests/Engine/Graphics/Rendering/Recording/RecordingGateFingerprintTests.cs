using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Recording;

[NonParallelizable]
[TestFixture]
public sealed class RecordingGateFingerprintTests
{
    private static readonly Rect s_bounds = new(0, 0, 100, 100);
    private static readonly Point s_inside = new(50, 50);
    private static readonly PixelSize s_frameSize = new(240, 160);

    [Test]
    public void TwoFragmentsDifferingOnlyInTheirHitTest_DoNotShareARecordingFingerprint()
    {
        using var hits = new HitTestSwappingNode(RenderHitTestContract.OutputBounds);
        using var misses = new HitTestSwappingNode(RenderHitTestContract.None);

        RenderFragmentReference hitting = OnlyFragmentOf(Record(hits));
        RenderFragmentReference missing = OnlyFragmentOf(Record(misses));

        Assert.Multiple(() =>
        {
            Assert.That(hitting.HitTest(s_inside), Is.True);
            Assert.That(missing.HitTest(s_inside), Is.False);
            Assert.That(hitting.RecordedBounds, Is.EqualTo(missing.RecordedBounds));
            Assert.That(
                missing.RecordingFingerprint,
                Is.Not.EqualTo(hitting.RecordingFingerprint),
                "the fingerprint speaks for the hit test, so it can decide reuse for a node that reads one");
        });
    }

    [Test]
    public void AChildThatChangesOnlyItsHitTest_StillForcesItsAncestorToRecordAgain()
    {
        using var child = new HitTestSwappingNode(RenderHitTestContract.OutputBounds);
        using var ancestor = new EmbedsInputHitTestNode();
        ancestor.AddChild(child);

        Record(ancestor);
        Record(ancestor);
        Assert.That(ancestor.ProcessCalls, Is.EqualTo(1), "an unchanged subtree must be served");

        long[] recordedOver = ancestor.RecordingSnapshot!.InputFingerprints.ToArray();
        child.MarkChanged();
        child.Contract = RenderHitTestContract.None;
        RenderFragmentReference embedded = OnlyFragmentOf(Record(ancestor), RenderFragmentKind.Opacity);

        Assert.Multiple(() =>
        {
            Assert.That(
                ancestor.RecordingSnapshot!.InputFingerprints,
                Is.Not.EqualTo(recordedOver),
                "the mechanism is a digest that no longer matches, not a rule about what re-recorded");
            Assert.That(ancestor.ProcessCalls, Is.EqualTo(2));
            Assert.That(
                embedded.HitTest(s_inside),
                Is.False,
                "the ancestor must not answer with the hit test the child no longer has");
        });
    }

    [Test]
    public void AServedAncestor_AnswersWithTheHitTestOfTheFragmentItIsReplayedOver()
    {
        using var child = new StatefulHitTestNode(s_bounds);
        using var ancestor = new EmbedsInputHitTestNode();
        ancestor.AddChild(child);

        Record(ancestor);
        Record(ancestor);

        long[] recordedOver = ancestor.RecordingSnapshot!.InputFingerprints.ToArray();
        child.MarkChanged();
        child.HitRegion = Rect.Empty;
        RenderFragmentReference embedded = OnlyFragmentOf(Record(ancestor), RenderFragmentKind.Opacity);

        Assert.Multiple(() =>
        {
            Assert.That(
                ancestor.RecordingSnapshot!.InputFingerprints,
                Is.EqualTo(recordedOver),
                "a contract that keeps its identity while its state moves must keep its digest");
            Assert.That(ancestor.ProcessCalls, Is.EqualTo(1), "so the ancestor is served");
            Assert.That(
                embedded.HitTest(s_inside),
                Is.False,
                "and the served fragment must read the hit test of the input it was replayed over");
        });
    }

    [Test]
    public void AParentThatBranchesOnItsInputHitTest_RecordsAgainWhenThatAnswerChanges()
    {
        using var child = new StatefulHitTestNode(s_bounds);
        using var parent = new BranchesOnInputHitTestNode();
        parent.AddChild(child);

        Record(parent);
        RecordedRenderGraph hitting = Record(parent);

        Assert.Multiple(() =>
        {
            Assert.That(parent.ProcessCalls, Is.EqualTo(1), "an unchanged subtree must still be served");
            Assert.That(
                KindOfPublishedFragment(hitting),
                Is.EqualTo(RenderFragmentKind.Opacity),
                "the input hits, so the parent takes its hitting branch");
        });

        long[] recordedOver = parent.RecordingSnapshot!.InputFingerprints.ToArray();
        child.MarkChanged();
        child.HitRegion = Rect.Empty;
        RecordedRenderGraph missing = Record(parent);

        Assert.Multiple(() =>
        {
            Assert.That(
                parent.RecordingSnapshot!.InputFingerprints,
                Is.EqualTo(recordedOver),
                "the digest cannot see this change, so the gate cannot be relying on it here");
            Assert.That(parent.ProcessCalls, Is.EqualTo(2), "the parent must record its other branch");
            Assert.That(
                KindOfPublishedFragment(missing),
                Is.EqualTo(RenderFragmentKind.OpaqueSource),
                "a parent served over a stale hit-test answer would publish the branch it no longer takes");
        });
    }

    [Test]
    public void AParentThatBranchesOnItsInputHitTest_IsStillServedWhenThatAnswerHolds()
    {
        using var child = new StatefulHitTestNode(s_bounds);
        using var parent = new BranchesOnInputHitTestNode();
        parent.AddChild(child);

        Record(parent);
        Record(parent);
        child.MarkChanged();
        child.HitRegion = s_bounds.Inflate(10);
        Record(parent);

        Assert.That(
            parent.ProcessCalls,
            Is.EqualTo(1),
            "the input still hits the point the parent read, so its recording still stands");
    }

    [Test]
    public void ANodeRecordedWithExplicitInputs_FollowsAHitTestAnswerItBranchedOn()
    {
        using var source = new StatefulHitTestNode(s_bounds);
        using var wrapper = new BranchesOnInputHitTestNode();
        using var driver = new DrivesAWrapperOverAnotherNode(source, wrapper);

        Record(driver);
        Record(driver);
        Assert.That(wrapper.ProcessCalls, Is.EqualTo(1), "an unchanged input serves the wrapper");

        source.MarkChanged();
        source.HitRegion = Rect.Empty;
        RecordedRenderGraph missing = Record(driver);

        Assert.Multiple(() =>
        {
            Assert.That(wrapper.ProcessCalls, Is.EqualTo(2));
            Assert.That(
                KindOfPublishedFragment(missing),
                Is.EqualTo(RenderFragmentKind.OpaqueSource));
        });
    }

    [Test]
    public void TheCrossCheck_NoLongerBlamesTheParentForAHitTestAnswerItsInputChanged()
    {
        if (!RenderRecordingCrossCheck.IsAvailable)
            Assert.Ignore("The cross-check call sites are compiled out of a Release build of Beutl.Engine.");

        using var child = new StatefulHitTestNode(s_bounds);
        using var parent = new BranchesOnInputHitTestNode();
        parent.AddChild(child);

        using (RenderRecordingCrossCheck.Enable())
        {
            Record(parent);
            child.MarkChanged();
            child.HitRegion = Rect.Empty;

            Assert.That(() => Record(parent), Throws.Nothing);
        }
    }

    [Test]
    public void ANodeRecordedWithExplicitInputs_FollowsAHitTestOnlyChangeInThoseInputs()
    {
        using var source = new HitTestSwappingNode(RenderHitTestContract.OutputBounds);
        using var wrapper = new EmbedsInputHitTestNode();
        using var driver = new DrivesAWrapperOverAnotherNode(source, wrapper);

        Record(driver);
        Record(driver);
        Assert.That(
            wrapper.ProcessCalls,
            Is.EqualTo(1),
            "a node reached with explicit inputs is served when those inputs digest to what it was recorded over");

        source.MarkChanged();
        source.Contract = RenderHitTestContract.None;
        RenderFragmentReference embedded = OnlyFragmentOf(Record(driver), RenderFragmentKind.Opacity);

        Assert.Multiple(() =>
        {
            Assert.That(
                embedded.HitTest(s_inside),
                Is.False,
                "a node served over matching input fingerprints must not keep a stale hit test");
            Assert.That(wrapper.ProcessCalls, Is.EqualTo(2), "the changed rule re-records it");
        });
    }

    [Test]
    public void TheCrossCheck_AcceptsAnAncestorWhoseInputChangedOnlyItsHitTest()
    {
        if (!RenderRecordingCrossCheck.IsAvailable)
            Assert.Ignore("The cross-check call sites are compiled out of a Release build of Beutl.Engine.");

        using var child = new HitTestSwappingNode(RenderHitTestContract.OutputBounds);
        using var ancestor = new EmbedsInputHitTestNode();
        ancestor.AddChild(child);

        using (RenderRecordingCrossCheck.Enable())
        {
            Record(ancestor);
            child.MarkChanged();
            child.Contract = RenderHitTestContract.None;

            Assert.That(() => Record(ancestor), Throws.Nothing);
        }
    }

    [Test]
    public void TheCrossCheck_AcceptsTheRepresentativeSceneWithOneLeafChangingEveryRequest()
    {
        if (!RenderRecordingCrossCheck.IsAvailable)
            Assert.Ignore("The cross-check call sites are compiled out of a Release build of Beutl.Engine.");

        RenderThread.Dispatcher.Invoke(static () =>
        {
            Drawable.Resource[] resources = CreateSceneResources();
            try
            {
                using var root = new DrawableRenderNode(resources[0]);
                RecordScene(root, resources);
                RenderNode leaf = CollectNodes(root).First(node => node is GeometryRenderNode);

                using (RenderRecordingCrossCheck.Enable())
                {
                    for (int frame = 0; frame < 4; frame++)
                    {
                        leaf.MarkChanged();
                        Assert.That(() => Record(root), Throws.Nothing);
                        ClearChanges(root);
                    }
                }
            }
            finally
            {
                foreach (Drawable.Resource resource in resources)
                    resource.Dispose();
            }
        });
    }

    [Test]
    public void ARepresentativeSceneWithOneChangingLeaf_ServesEveryAncestorWhoseInputsDigestTheSame()
    {
        (int served, int rejected, int refused) = RenderThread.Dispatcher.Invoke(static () =>
        {
            Drawable.Resource[] resources = CreateSceneResources();
            try
            {
                using var root = new DrawableRenderNode(resources[0]);
                RecordScene(root, resources);
                List<RenderNode> nodes = CollectNodes(root);
                RenderNode leaf = nodes.First(node => node is GeometryRenderNode);

                for (int frame = 0; frame < 4; frame++)
                {
                    leaf.MarkChanged();
                    Record(root);
                    ClearChanges(root);
                }

                var before = new RenderNodeRecordingSnapshot?[nodes.Count];
                for (int index = 0; index < nodes.Count; index++)
                    before[index] = nodes[index].RecordingSnapshot;

                leaf.MarkChanged();
                Record(root);

                int servedCount = 0, rejectedCount = 0, refusedCount = 0;
                for (int index = 0; index < nodes.Count; index++)
                {
                    RenderNodeRecordingSnapshot? after = nodes[index].RecordingSnapshot;
                    bool retained = ReferenceEquals(before[index], after);
                    if (after is null)
                        continue;
                    if (!after.IsReplayable)
                        refusedCount++;
                    else if (retained)
                        servedCount++;
                    else
                        rejectedCount++;
                }

                return (servedCount, rejectedCount, refusedCount);
            }
            finally
            {
                foreach (Drawable.Resource resource in resources)
                    resource.Dispose();
            }
        });

        TestContext.Out.WriteLine($"served={served} rejected={rejected} refused={refused}");
        Assert.Multiple(() =>
        {
            Assert.That(
                rejected,
                Is.Zero,
                "no ancestor may lose its recording only because a descendant re-recorded");
            Assert.That(
                served,
                Is.EqualTo(20),
                "every replayable node in the scene is served, including the six the descendant rule rejected");
            Assert.That(refused, Is.EqualTo(3), "the two geometries and the text still refuse to be stored");
        });
    }

    private static Drawable.Resource[] CreateSceneResources()
    {
        var background = new RectShape
        {
            Width = { CurrentValue = s_frameSize.Width },
            Height = { CurrentValue = s_frameSize.Height },
            Fill = { CurrentValue = Brushes.CornflowerBlue },
        };

        var accent = new EllipseShape
        {
            Width = { CurrentValue = 76 },
            Height = { CurrentValue = 76 },
            Fill = { CurrentValue = Brushes.OrangeRed },
            FilterEffect = { CurrentValue = new Brightness { Amount = { CurrentValue = 78 } } },
            Transform = { CurrentValue = new TranslateTransform(44, -18) },
        };

        var label = new TextBlock
        {
            FontFamily = { CurrentValue = FontFamily.Default },
            Size = { CurrentValue = 28 },
            Fill = { CurrentValue = Brushes.White },
            Text = { CurrentValue = "CACHE" },
            Transform = { CurrentValue = new TranslateTransform(-28, 30) },
        };

        CompositionContext context = CompositionContext.Default;
        return
        [
            background.ToResource(context),
            accent.ToResource(context),
            label.ToResource(context),
        ];
    }

    private static void RecordScene(DrawableRenderNode root, Drawable.Resource[] resources)
    {
        using var context = new GraphicsContext2D(root, s_frameSize.ToSize(1));
        context.Clear();
        foreach (Drawable.Resource resource in resources)
            context.DrawDrawable(resource);
    }

    private static List<RenderNode> CollectNodes(RenderNode root)
    {
        var seen = new HashSet<RenderNode>(ReferenceEqualityComparer.Instance);
        var result = new List<RenderNode>();
        Visit(root);
        return result;

        void Visit(RenderNode node)
        {
            if (node.IsDisposed || !seen.Add(node))
                return;

            result.Add(node);
            foreach (RenderNode child in node.ChildNodes)
                Visit(child);
        }
    }

    private static void ClearChanges(RenderNode root)
    {
        foreach (RenderNode node in CollectNodes(root))
            node.ClearChanges(node.ChangeVersion);
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

    private static RenderFragmentKind KindOfPublishedFragment(RecordedRenderGraph graph)
    {
        RenderFragmentId root = graph.PublicationRoots.Single();
        foreach (RecordedRenderFragment fragment in graph.Fragments)
        {
            if (fragment.Id == root)
                return ((RenderFragmentReference)fragment.Payload!).Kind;
        }

        throw new InvalidOperationException("The recorded graph has no published fragment.");
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

    /// <summary>Records one bounds with a swappable hit-test contract, so only the rule moves.</summary>
    private sealed class HitTestSwappingNode(RenderHitTestContract contract) : RenderNode
    {
        // Deliberately not raising HasChanges: the tests that move it mark the node themselves.
#pragma warning disable BESG005
        public RenderHitTestContract Contract { get; set; } = contract;
#pragma warning restore BESG005

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

    /// <summary>Records one bounds with a fixed rule whose state moves, the way a shape's fill does.</summary>
    private sealed class StatefulHitTestNode(Rect hitRegion) : RenderNode
    {
        // Deliberately not raising HasChanges: the tests that move it mark the node themselves.
#pragma warning disable BESG005
        public Rect HitRegion { get; set; } = hitRegion;
#pragma warning restore BESG005

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
                hitTest: RenderHitTestContract.Custom(
                    HitRegion,
                    static (region, _, point) => region.Contains(point)),
                scale: RenderScaleContract.Vector,
                deviceGridSensitivity: RenderDeviceGridSensitivity.Insensitive)));
        }
    }

    /// <summary>Wraps every input the way the built-in combinators do.</summary>
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

    /// <summary>Decides what to record from the hit test of each input, the way a public author may.</summary>
    private sealed class BranchesOnInputHitTestNode : ContainerRenderNode
    {
        public int ProcessCalls { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            ProcessCalls++;
            foreach (RenderFragmentHandle input in context.Inputs)
            {
                input.TryHitTest(s_inside, out bool hit);
                context.Publish(hit ? context.Opacity(input, 0.5f) : context.ContributeValues(input));
            }
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
