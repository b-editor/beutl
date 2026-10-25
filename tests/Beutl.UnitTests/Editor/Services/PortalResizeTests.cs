using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.TestInfrastructure;
using static Beutl.UnitTests.TestInfrastructure.TrimTestMedia;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
[NonParallelizable]
public class PortalResizeTests
{
    [OneTimeSetUp]
    public void RegisterDecoder() => TestMediaHelper.RegisterTestDecoder();

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void Portal_HasNoOriginalDurationOrMaximumLength(int providers)
    {
        using var harness = new SceneHistoryHarness("beutl_portal_resize");
        Element element = AddPortal(harness, providers);

        Assert.Multiple(() =>
        {
            Assert.That(SlippableMedia.CreateResizeConstraints(element).GetMaximumDuration(), Is.Null);
            Assert.That(SlippableMedia.HasOriginalDuration(element), Is.False);
            Assert.That(SlippableMedia.GetOriginalDuration(element), Is.Null);
        });
    }

    [TestCase(true, 1d, 4d)]
    [TestCase(true, 3d, 2d)]
    [TestCase(false, 2d, 5d)]
    [TestCase(false, 2d, 1d)]
    public void CountTen_PreviewCommitAndUndoPreserveRequestedEdges(bool leftEdge, double start, double length)
    {
        using var clamp = new ClampResizeToOriginalLengthScope(true);
        using var harness = new SceneHistoryHarness("beutl_portal_resize");
        Element element = AddPortal(harness, 2);
        var constraints = SlippableMedia.CreateResizeConstraints(element);
        Assert.That(leftEdge ? constraints.ClampStart(Seconds(start)) : constraints.ClampLength(Seconds(length)),
            Is.EqualTo(Seconds(leftEdge ? start : length)));

        new ElementResizeService(harness.History).Resize(harness.Scene, [new(element, Seconds(start), Seconds(length), 0)]);
        Assert.Multiple(() =>
        {
            Assert.That(element.Start.Value, Is.EqualTo(Seconds(start)));
            Assert.That(element.Length.Value, Is.EqualTo(Seconds(length)));
            Assert.That(harness.Scene.Children.Skip(1).Select(child => child.Length.Value), Is.All.EqualTo(Seconds(3)));
        });
        harness.History.Undo();
        Assert.Multiple(() =>
        {
            Assert.That(element.Start.Value, Is.EqualTo(Seconds(2)));
            Assert.That(element.Length.Value, Is.EqualTo(Seconds(3)));
        });
    }

    [TestCase("SourceVideo", 0)]
    [TestCase("SourceVideo", 8)]
    [TestCase("SourceSound", 0)]
    public void LocalMedia_KeepsItsOwnDurationLimit(string mediaType, int disabledControllers)
    {
        using var clamp = new ClampResizeToOriginalLengthScope(true);
        using var harness = new SceneHistoryHarness("beutl_portal_resize");
        Element element = AddPortal(harness, 2);
        var (_, offset) = AddMedia(element, mediaType, speed: 200, offsetSeconds: 1, durationSeconds: 6);
        for (int i = 0; i < disabledControllers; i++)
            element.Objects.Add(new DrawableTimeController { IsEnabled = { Value = false } });
        Assert.That(SlippableMedia.GetOriginalDuration(element), Is.EqualTo(Seconds(2.5)));

        new ElementResizeService(harness.History).Resize(harness.Scene, [new(element, Seconds(2), Seconds(5), 0)]);
        Assert.Multiple(() =>
        {
            Assert.That(element.Length.Value, Is.EqualTo(Seconds(3)), "An existing overrun can shrink but must not grow.");
            Assert.That(offset.Value, Is.EqualTo(Seconds(1)));
        });
        IElementResizeService resize = new ElementResizeService(harness.History);
        Assert.That(resize.ResizeToOriginalDuration(harness.Scene, element), Is.True);
        Assert.That(element.Length.Value, Is.EqualTo(Seconds(2.5)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Controller_OnlyHasAFixedDurationWhenItsLocalInputIsKnown(bool localBeforePortal)
    {
        using var harness = new SceneHistoryHarness("beutl_portal_resize");
        Element element = AddPortal(harness, 2);
        var video = CreateVideo(6);
        element.Objects.Insert(localBeforePortal ? 0 : 1, video);
        element.Objects.Add(new DrawableTimeController { Speed = { Value = 200 } });
        foreach (Element provider in harness.Scene.Children.Where(child => child != element)) provider.Length.Value = Seconds(1);
        using var compositor = CreateCompositor(harness.Scene);
        var before = compositor.EvaluateGraphics(Seconds(2.5)).Objects.OfType<DrawableTimeController.Resource>().Single();
        Assert.That(ReferenceEquals(before.Target!.GetOriginal(), video), Is.EqualTo(localBeforePortal));
        var after = compositor.EvaluateGraphics(Seconds(3.5)).Objects.OfType<DrawableTimeController.Resource>().Single();
        Assert.That(after.Target!.GetOriginal(), Is.SameAs(video));

        Assert.That(SlippableMedia.CreateResizeConstraints(element).GetMaximumDuration(),
            Is.EqualTo(localBeforePortal ? Seconds(3) : (MediaTime?)null));
        Assert.That(SlippableMedia.GetOriginalDuration(element),
            Is.EqualTo(localBeforePortal ? Seconds(3) : (MediaTime?)null));
        Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
    }

    private static Element AddPortal(SceneHistoryHarness harness, int providers)
    {
        Element element = harness.AddElement(Seconds(2), Seconds(3));
        element.Objects.Add(new PortalObject { Count = { Value = 10 } });
        for (int i = 0; i < providers; i++)
            harness.AddElement(Seconds(2), Seconds(3), i + 1).Objects.Add(new RectShape());
        return element;
    }
}
