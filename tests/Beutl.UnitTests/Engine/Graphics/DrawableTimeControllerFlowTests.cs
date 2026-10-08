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
        var source = new VideoSource();
        source.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 300)));
        var video = new SourceVideo { Source = { CurrentValue = source } };
        Element element = harness.AddElement(TimeSpan.Zero, Seconds(4));
        element.Objects.Add(video);
        Drawable preceding = kind switch
        {
            "group" => new DrawableGroup(),
            "decorator" => new DrawableDecorator(),
            _ => new DrawableTimeController { Speed = { CurrentValue = 200 } }
        };
        element.Objects.Add(preceding);
        var controller = new DrawableTimeController { OffsetPosition = { CurrentValue = Seconds(1) } };
        element.Objects.Add(controller);
        using var compositor = new SceneCompositor(harness.Scene) { DisableResourceShare = disableShare, ForceOriginalSource = true };

        var first = (DrawableTimeController.Resource)compositor.EvaluateGraphics(Seconds(0.5)).Objects.Single();
        var firstVideo = Videos(first).Single();
        double firstPosition = firstVideo.RequestedPosition.TotalSeconds;
        var second = (DrawableTimeController.Resource)compositor.EvaluateGraphics(Seconds(1)).Objects.Single();
        var secondVideo = Videos(second).Single();

        Assert.Multiple(() =>
        {
            Assert.That(firstVideo.RequireOriginal(), Is.SameAs(video));
            Assert.That(firstPosition, Is.EqualTo(kind == "controller" ? 3 : 1.5).Within(0.000001));
            Assert.That(secondVideo.RequireOriginal(), Is.SameAs(video));
            Assert.That(secondVideo.RequestedPosition.TotalSeconds, Is.EqualTo(kind == "controller" ? 4 : 2).Within(0.000001));
        });
    }

    [Test]
    public void FlowTarget_ReplacingInputsDisposesReplayCopiesWithoutDisposingTheirProducers()
    {
        using var harness = new SceneHistoryHarness("beutl_controller_flow_ownership", duration: Seconds(10));
        var first = CreateVideo();
        var second = CreateVideo();
        Element element = harness.AddElement(TimeSpan.Zero, Seconds(4));
        element.Objects.Add(first);
        element.Objects.Add(second);
        element.Objects.Add(new DrawableGroup());
        var controller = new DrawableTimeController { IsEnabled = false, OffsetPosition = { CurrentValue = Seconds(1) } };
        element.Objects.Add(controller);
        using var compositor = new SceneCompositor(harness.Scene) { ForceOriginalSource = true };
        var producerGroup = (DrawableGroup.Resource)compositor.EvaluateGraphics(Seconds(0.5)).Objects.Single();
        var producer = Videos(producerGroup).First();
        controller.IsEnabled = true;
        var firstReplay = Videos(compositor.EvaluateGraphics(Seconds(0.5)).Objects.Single()).First();
        double producerPosition = producer.RequestedPosition.TotalSeconds;
        first.IsEnabled = false;
        var replacement = Videos(compositor.EvaluateGraphics(Seconds(0.5)).Objects.Single()).Single();

        Assert.Multiple(() =>
        {
            Assert.That(firstReplay, Is.Not.SameAs(producer));
            Assert.That(producerPosition, Is.EqualTo(0.5).Within(0.000001));
            Assert.That(firstReplay.IsDisposed, Is.True);
            Assert.That(producer.IsDisposed, Is.False);
            Assert.That(replacement.RequireOriginal(), Is.SameAs(second));
            Assert.That(replacement.RequestedPosition.TotalSeconds, Is.EqualTo(1.5).Within(0.000001));
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
        Element element = harness.AddElement(TimeSpan.Zero, Seconds(4));
        element.Objects.Add(first);
        element.Objects.Add(new DrawableGroup());
        element.Objects.Add(sibling);
        element.Objects.Add(new DrawableTimeController { OffsetPosition = { CurrentValue = Seconds(1) } });
        using var compositor = new SceneCompositor(harness.Scene) { ForceOriginalSource = true };

        var resources = compositor.EvaluateGraphics(Seconds(0.5)).Objects.SelectMany(Videos).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(resources, Has.Length.EqualTo(2));
            Assert.That(resources.Single(resource => ReferenceEquals(resource.RequireOriginal(), first)).RequestedPosition.TotalSeconds,
                Is.EqualTo(1.5).Within(0.000001));
            Assert.That(resources.Single(resource => ReferenceEquals(resource.RequireOriginal(), sibling)).RequestedPosition.TotalSeconds,
                Is.EqualTo(0.5).Within(0.000001));
        });
    }

    private static SourceVideo CreateVideo()
    {
        var source = new VideoSource();
        source.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 300)));
        return new SourceVideo { Source = { CurrentValue = source } };
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

    private static TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);
}
