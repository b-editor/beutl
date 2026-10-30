using Beutl.Collections.Pooled;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Engine.Graphics;

[TestFixture]
public class DrawableTimeControllerFlowTests
{
    [OneTimeSetUp]
    public void RegisterDecoder() => TestMediaHelper.RegisterTestDecoder();

    [TestCase("group", false)]
    [TestCase("group", true)]
    [TestCase("decorator", false)]
    [TestCase("decorator", true)]
    [TestCase("controller", false)]
    [TestCase("controller", true)]
    public void FlowTarget_ReevaluatesTheConsumedInputsAtTheMappedTime(string kind, bool disableShare)
    {
        using var harness = new SceneHistoryHarness("beutl_controller_flow", duration: Seconds(10));
        var video = CreateVideo();
        Element element = harness.AddElement(MediaTime.Zero, Seconds(4));
        element.Objects.Add(video);
        Drawable preceding = kind switch
        {
            "group" => new DrawableGroup(),
            "decorator" => new DrawableDecorator(),
            _ => new DrawableTimeController { Speed = { Value = 200 } }
        };
        element.Objects.Add(preceding);
        var controller = new DrawableTimeController { OffsetPosition = { Value = Seconds(1) } };
        element.Objects.Add(controller);
        using var compositor = new SceneCompositor(harness.Scene) { DisableResourceShare = disableShare, ForceOriginalSource = true };

        var first = (DrawableTimeController.Resource)compositor.EvaluateGraphics(Seconds(0.5)).Objects.Single();
        var firstVideo = Videos(first).Single();
        double firstPosition = firstVideo.RequestedPosition.ToSeconds();
        var second = (DrawableTimeController.Resource)compositor.EvaluateGraphics(Seconds(1)).Objects.Single();
        var secondVideo = Videos(second).Single();

        Assert.Multiple(() =>
        {
            Assert.That(firstVideo.RequireOriginal(), Is.SameAs(video));
            Assert.That(firstPosition, Is.EqualTo(kind == "controller" ? 3 : 1.5).Within(0.000001));
            Assert.That(secondVideo.RequireOriginal(), Is.SameAs(video));
            Assert.That(secondVideo.RequestedPosition.ToSeconds(), Is.EqualTo(kind == "controller" ? 4 : 2).Within(0.000001));
        });
    }

    [Test]
    public void FlowTarget_RetimingKeepsSameClipInputsButNotPortalClips()
    {
        using var harness = new SceneHistoryHarness("beutl_controller_clip_coverage", duration: Seconds(10));
        Element element = harness.AddElement(Seconds(1), Seconds(2));
        Element portalElement = harness.AddElement(Seconds(1), Seconds(2), zIndex: 1);
        var local = CreateVideo();
        var portal = CreateVideo();
        portalElement.Objects.Add(portal);
        element.Objects.Add(new PortalObject { Count = { Value = 1 } });
        element.Objects.Add(local);
        element.Objects.Add(new DrawableGroup());
        var controller = new DrawableTimeController();
        element.Objects.Add(controller);
        using var compositor = new SceneCompositor(harness.Scene) { ForceOriginalSource = true };

        foreach (bool useOffset in new[] { false, true })
        {
            controller.Speed.Value = useOffset ? 100 : 200;
            controller.OffsetPosition.Value = useOffset ? Seconds(1) : MediaTime.Zero;
            foreach (double time in new[] { 1.25, 1.99, 2, 2.5, 1.25 })
            {
                var sources = Videos(compositor.EvaluateGraphics(Seconds(time)).Objects.OfType<DrawableTimeController.Resource>().Single())
                    .Select(resource => resource.RequireOriginal()).ToArray();
                Assert.Multiple(() =>
                {
                    Assert.That(sources, Does.Contain(local), $"same-clip content at {time}, offset: {useOffset}");
                    Assert.That(sources.Contains(portal), Is.EqualTo(time < 2),
                        "A separate clip with the same authored range still ends at the mapped clock's exclusive end.");
                });
            }
        }
    }

    [Test]
    public void FlowTarget_ReplacingInputsDisposesReplayCopiesWithoutDisposingTheirProducers()
    {
        using var harness = new SceneHistoryHarness("beutl_controller_flow_ownership", duration: Seconds(10));
        var first = CreateVideo();
        var second = CreateVideo();
        Element element = harness.AddElement(MediaTime.Zero, Seconds(4));
        element.Objects.Add(first);
        element.Objects.Add(second);
        element.Objects.Add(new DrawableGroup());
        var controller = new DrawableTimeController { IsEnabled = { Value = false }, OffsetPosition = { Value = Seconds(1) } };
        element.Objects.Add(controller);
        using var compositor = new SceneCompositor(harness.Scene) { ForceOriginalSource = true };
        var producerGroup = (DrawableGroup.Resource)compositor.EvaluateGraphics(Seconds(0.5)).Objects.Single();
        var producer = Videos(producerGroup).First();
        controller.IsEnabled.Value = true;
        var firstReplay = Videos(compositor.EvaluateGraphics(Seconds(0.5)).Objects.Single()).First();
        double producerPosition = producer.RequestedPosition.ToSeconds();
        first.IsEnabled.Value = false;
        var replacement = Videos(compositor.EvaluateGraphics(Seconds(0.5)).Objects.Single()).Single();

        Assert.Multiple(() =>
        {
            Assert.That(firstReplay, Is.Not.SameAs(producer));
            Assert.That(producerPosition, Is.EqualTo(0.5).Within(0.000001));
            Assert.That(firstReplay.IsDisposed, Is.True);
            Assert.That(producer.IsDisposed, Is.False);
            Assert.That(replacement.RequireOriginal(), Is.SameAs(second));
            Assert.That(replacement.RequestedPosition.ToSeconds(), Is.EqualTo(1.5).Within(0.000001));
        });
        compositor.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(replacement.IsDisposed, Is.True);
            Assert.That(producer.IsDisposed, Is.True);
        });
    }

    [Test]
    public void FlowTarget_ReevaluationLeavesTheRemainingCallerFlowUntouched()
    {
        using var harness = new SceneHistoryHarness("beutl_controller_flow_sibling", duration: Seconds(10));
        var first = CreateVideo();
        var sibling = CreateVideo();
        Element element = harness.AddElement(MediaTime.Zero, Seconds(4));
        element.Objects.Add(first);
        element.Objects.Add(new DrawableGroup());
        element.Objects.Add(sibling);
        element.Objects.Add(new DrawableTimeController { OffsetPosition = { Value = Seconds(1) } });
        using var compositor = new SceneCompositor(harness.Scene) { ForceOriginalSource = true };

        var resources = compositor.EvaluateGraphics(Seconds(0.5)).Objects.SelectMany(Videos).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(resources, Has.Length.EqualTo(2));
            Assert.That(resources.Single(resource => ReferenceEquals(resource.RequireOriginal(), first)).RequestedPosition.ToSeconds(),
                Is.EqualTo(1.5).Within(0.000001));
            Assert.That(resources.Single(resource => ReferenceEquals(resource.RequireOriginal(), sibling)).RequestedPosition.ToSeconds(),
                Is.EqualTo(0.5).Within(0.000001));
        });
    }

    [TestCase("group")]
    [TestCase("decorator")]
    [TestCase("controller")]
    public void FlowSnapshots_UnchangedProvenanceIsReusedAcrossFrames(string kind)
    {
        using var harness = new SceneHistoryHarness("beutl_flow_snapshot_reuse", duration: Seconds(10));
        Element element = harness.AddElement(MediaTime.Zero, Seconds(4));
        var video = CreateVideo();
        element.Objects.Add(video);
        element.Objects.Add(kind switch
        {
            "group" => new DrawableGroup(),
            "decorator" => new DrawableDecorator(),
            _ => new DrawableTimeController { Speed = { Value = 200 } }
        });
        using var compositor = new SceneCompositor(harness.Scene) { ForceOriginalSource = true };
        var first = FlowNode.Capture(compositor.EvaluateGraphics(Seconds(0.5)).Objects.Single());
        var secondResource = compositor.EvaluateGraphics(Seconds(1)).Objects.Single();
        var second = FlowNode.Capture(secondResource);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.SameAs(first), "Changing playback time must not rebuild unchanged Flow provenance.");
            Assert.That(Videos(secondResource).Single().RequestedPosition.ToSeconds(),
                Is.EqualTo(kind == "controller" ? 2 : 1).Within(0.000001));
        });
    }

    [Test]
    public void FlowSnapshots_ChangedNestedInputsInvalidateAncestorsWithoutMutatingTheOldSnapshot()
    {
        using var harness = new SceneHistoryHarness("beutl_flow_snapshot_invalidation", duration: Seconds(10));
        var first = CreateVideo();
        var second = CreateVideo();
        Element element = harness.AddElement(MediaTime.Zero, Seconds(4));
        element.Objects.Add(first);
        element.Objects.Add(second);
        element.Objects.Add(new DrawableGroup());
        element.Objects.Add(new DrawableTimeController());
        using var compositor = new SceneCompositor(harness.Scene) { ForceOriginalSource = true };
        var before = FlowNode.Capture(compositor.EvaluateGraphics(Seconds(0.5)).Objects.Single())!;
        first.IsEnabled.Value = false;
        var after = FlowNode.Capture(compositor.EvaluateGraphics(Seconds(1)).Objects.Single())!;

        Assert.Multiple(() =>
        {
            Assert.That(after, Is.Not.SameAs(before));
            Assert.That(before.Inputs.Single().Inputs.Select(node => node.Object), Is.EqualTo(new[] { first, second }));
            Assert.That(after.Inputs.Single().Inputs.Select(node => node.Object), Is.EqualTo(new[] { second }));
        });
    }

    [Test]
    public void FlowInputState_StableInputsDoNotAllocateAfterWarmup()
    {
        var owner = new DrawableGroup();
        using var first = new EngineObject().ToResource(CompositionContext.Default);
        using var second = new EngineObject().ToResource(CompositionContext.Default);
        var flow = new List<EngineObject.Resource>(2);
        var context = new CompositionContext(MediaTime.Zero) { Flow = flow };
        using var state = new FlowInputState();
        using var consumed = new PooledList<EngineObject.Resource>();
        for (int i = 0; i < 16; i++)
        {
            flow.Add(first);
            flow.Add(second);
            consumed.Clear();
            state.Collect(context, owner, consumed);
        }
        var snapshot = state.Inputs;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            flow.Add(first);
            flow.Add(second);
            consumed.Clear();
            state.Collect(context, owner, consumed);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Multiple(() =>
        {
            Assert.That(state.Inputs, Is.SameAs(snapshot));
            Assert.That(allocated, Is.Zero);
        });
    }

    private static SourceVideo CreateVideo()
    {
        var source = new VideoSource();
        source.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 300)));
        return new SourceVideo { Source = { Value = source } };
    }

    private static IEnumerable<SourceVideo.Resource> Videos(EngineObject.Resource resource)
    {
        if (resource is SourceVideo.Resource video) yield return video;
        else if (resource is DrawableTimeController.Resource controller && controller.Target != null)
            foreach (var target in Videos(controller.Target)) yield return target;
        else if (resource is DrawableGroup.Resource group)
            foreach (var child in group.Children)
                foreach (var target in Videos(child)) yield return target;
        else if (resource is DrawableDecorator.Resource decorator)
            foreach (var child in decorator.Children)
                foreach (var target in Videos(child)) yield return target;
    }

    private static MediaTime Seconds(double seconds) => MediaTime.FromSeconds(seconds);
}
