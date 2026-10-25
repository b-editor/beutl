using Beutl.Audio;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.TestInfrastructure;
using static Beutl.UnitTests.TestInfrastructure.TrimTestMedia;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
[NonParallelizable]
public class PortalResizeTests
{
    private SceneHistoryHarness _harness = null!;
    private ClampResizeToOriginalLengthScope _clamp = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp() => TestMediaHelper.RegisterTestDecoder();

    [SetUp]
    public void Setup()
    {
        _clamp = new ClampResizeToOriginalLengthScope(true);
        _harness = new SceneHistoryHarness("beutl_portal_resize", start: MediaTime.Zero, duration: Seconds(30));
    }

    [TearDown]
    public void TearDown()
    {
        _clamp.Dispose();
        _harness.Dispose();
    }

    [Test]
    public void CountTenPortal_LeftEdgeGrow_KeepsTheRequestedInterval()
    {
        AddProviders(10, Seconds(3));
        Element element = AddPortal(Seconds(2), Seconds(2), count: 10);

        var constraints = SlippableMedia.CreateResizeConstraints(element);
        Assert.That(constraints.ClampStart(MediaTime.Zero), Is.EqualTo(MediaTime.Zero));
        Assert.That(constraints.GetMaximumDuration(), Is.Null);

        new ElementResizeService(_harness.History).Resize(_harness.Scene,
            [new(element, MediaTime.Zero, Seconds(4), 0)]);

        Assert.That(element.Start.Value, Is.EqualTo(MediaTime.Zero));
        Assert.That(element.Length.Value, Is.EqualTo(Seconds(4)));
    }

    [Test]
    public void CountTenPortal_LeftEdgeShrink_KeepsTheRequestedInterval()
    {
        AddProviders(10, Seconds(6));
        Element element = AddPortal(MediaTime.Zero, Seconds(4), count: 10);

        var constraints = SlippableMedia.CreateResizeConstraints(element);
        Assert.That(constraints.ClampStart(Seconds(2)), Is.EqualTo(Seconds(2)));

        new ElementResizeService(_harness.History).Resize(_harness.Scene,
            [new(element, Seconds(2), Seconds(2), 0)]);

        Assert.That(element.Start.Value, Is.EqualTo(Seconds(2)));
        Assert.That(element.Length.Value, Is.EqualTo(Seconds(2)));
    }

    [TestCase(0)]
    [TestCase(5)]
    public void EmptyPortal_MaximumDurationIsUnbounded(double startSeconds)
    {
        Element element = AddPortal(Seconds(startSeconds), Seconds(1), count: 0);
        var constraints = SlippableMedia.CreateResizeConstraints(element);

        MediaTime? maximum = null;
        Assert.DoesNotThrow(() => maximum = constraints.GetMaximumDuration());
        Assert.Multiple(() =>
        {
            Assert.That(maximum, Is.Null);
            Assert.That(constraints.ClampLength(Seconds(20)), Is.EqualTo(Seconds(20)));
            Assert.That(SlippableMedia.HasOriginalDuration(element), Is.False);
            Assert.That(SlippableMedia.GetOriginalDuration(element), Is.Null);
        });
    }

    [Test]
    public void ImportedMedia_DoesNotLimitThePortalLength()
    {
        AddProviders(10, Seconds(1));
        Element element = AddPortal(MediaTime.Zero, Seconds(1), count: 10);

        var constraints = SlippableMedia.CreateResizeConstraints(element);
        Assert.Multiple(() =>
        {
            Assert.That(constraints.GetMaximumDuration(), Is.Null);
            Assert.That(constraints.ClampLength(Seconds(8)), Is.EqualTo(Seconds(8)));
            Assert.That(SlippableMedia.GetOriginalDuration(element), Is.Null);
        });
    }

    [Test]
    public void LocalMedia_StillLimitsThePortalLength()
    {
        AddProviders(10, Seconds(1));
        Element element = AddPortal(MediaTime.Zero, Seconds(1), count: 10, CreateVideo(6));

        var constraints = SlippableMedia.CreateResizeConstraints(element);
        Assert.That(constraints.GetMaximumDuration(), Is.EqualTo(Seconds(6)));
        Assert.That(constraints.ClampLength(Seconds(8)), Is.EqualTo(Seconds(6)));
    }

    [Test]
    public void LocalMediaAfterThePortal_IsNotMappedThroughTheImportedInput()
    {
        Element provider = AddProviders(1, Seconds(20))[0];
        SourceVideo local = CreateVideo(6);
        Element element = AddPortal(MediaTime.Zero, Seconds(1), count: 1, local,
            new DrawableTimeController { Speed = { Value = 200 } });

        using var compositor = CreateCompositor(_harness.Scene);
        DrawableTimeController.Resource controller = compositor.EvaluateGraphics(MediaTime.Zero)
            .Objects.OfType<DrawableTimeController.Resource>().Single();
        Assert.That(controller.Target!.GetOriginal(), Is.SameAs(provider.Objects[0]),
            "The controller consumes the imported clip; the local clip stays a separate input.");

        var constraints = SlippableMedia.CreateResizeConstraints(element);
        Assert.That(constraints.GetMaximumDuration(), Is.EqualTo(Seconds(6)));
        Assert.That(constraints.ClampLength(Seconds(8)), Is.EqualTo(Seconds(6)));
    }

    [Test]
    public void LocalMediaAfterAnEmptyPortal_IsMappedThroughItsController()
    {
        SourceVideo local = CreateVideo(6);
        Element element = AddPortal(MediaTime.Zero, Seconds(1), count: 0, local,
            new DrawableTimeController { Speed = { Value = 200 } });

        var constraints = SlippableMedia.CreateResizeConstraints(element);
        Assert.That(constraints.GetMaximumDuration(), Is.EqualTo(Seconds(3)));
        Assert.That(constraints.ClampLength(Seconds(8)), Is.EqualTo(Seconds(3)));
    }

    [Test]
    public void PortalWithoutProvider_MapsLocalMediaThroughItsController()
    {
        SourceVideo local = CreateVideo(6);
        Element element = AddPortal(MediaTime.Zero, Seconds(1), count: 1, local,
            new DrawableTimeController { Speed = { Value = 200 } });

        using var compositor = CreateCompositor(_harness.Scene);
        DrawableTimeController.Resource controller = compositor.EvaluateGraphics(MediaTime.Zero)
            .Objects.OfType<DrawableTimeController.Resource>().Single();
        Assert.That(controller.Target!.GetOriginal(), Is.SameAs(local));

        var constraints = SlippableMedia.CreateResizeConstraints(element);
        Assert.That(constraints.GetMaximumDuration(), Is.EqualTo(Seconds(3)));
        Assert.That(constraints.ClampLength(Seconds(8)), Is.EqualTo(Seconds(3)));
    }

    [Test]
    public void AudioOnlyProvider_MapsLocalDrawableThroughItsController()
    {
        Element provider = _harness.AddElement(MediaTime.Zero, Seconds(20));
        AddMedia(provider, nameof(SourceSound), speed: 100, durationSeconds: 20);
        provider.ZIndex.Value = 1;
        SourceVideo local = CreateVideo(6);
        Element element = AddPortal(MediaTime.Zero, Seconds(1), count: 1, local,
            new DrawableTimeController { Speed = { Value = 200 } });

        using var compositor = CreateCompositor(_harness.Scene);
        DrawableTimeController.Resource controller = compositor.EvaluateGraphics(MediaTime.Zero)
            .Objects.OfType<DrawableTimeController.Resource>().Single();
        Assert.That(controller.Target!.GetOriginal(), Is.SameAs(local));

        var constraints = SlippableMedia.CreateResizeConstraints(element);
        Assert.That(constraints.GetMaximumDuration(), Is.EqualTo(Seconds(3)));
        Assert.That(constraints.ClampLength(Seconds(8)), Is.EqualTo(Seconds(3)));
    }

    [Test]
    public void GroupedLocalMediaAfterPortal_IsMappedThroughItsController()
    {
        AddProviders(1, Seconds(20));
        SourceVideo local = CreateVideo(6);
        var group = new DrawableGroup();
        Element element = AddPortal(MediaTime.Zero, Seconds(1), count: 1, local, group,
            new DrawableTimeController { Speed = { Value = 200 } });

        using var compositor = CreateCompositor(_harness.Scene);
        DrawableTimeController.Resource controller = compositor.EvaluateGraphics(MediaTime.Zero)
            .Objects.OfType<DrawableTimeController.Resource>().Single();
        Assert.That(controller.Target, Is.Not.Null);

        var constraints = SlippableMedia.CreateResizeConstraints(element);
        Assert.That(constraints.GetMaximumDuration(), Is.EqualTo(Seconds(3)));
        Assert.That(constraints.ClampLength(Seconds(8)), Is.EqualTo(Seconds(3)));
    }

    [Test]
    public void MultipleImportedDrawables_DoNotStealTheLocalControllerInput()
    {
        Element provider = _harness.AddElement(MediaTime.Zero, Seconds(20));
        provider.Objects.Add(CreateVideo(20));
        provider.Objects.Add(CreateVideo(20));
        provider.ZIndex.Value = 1;
        SourceVideo local = CreateVideo(6);
        Element element = AddPortal(MediaTime.Zero, Seconds(1), count: 1, local,
            new DrawableTimeController { Speed = { Value = 200 } },
            new DrawableTimeController { Speed = { Value = 200 } });

        using var compositor = CreateCompositor(_harness.Scene);
        DrawableTimeController.Resource[] controllers = compositor.EvaluateGraphics(MediaTime.Zero)
            .Objects.OfType<DrawableTimeController.Resource>().ToArray();
        Assert.That(controllers, Has.Length.EqualTo(2));
        Assert.That(controllers[0].Target!.GetOriginal(), Is.SameAs(provider.Objects[0]));
        Assert.That(controllers[1].Target!.GetOriginal(), Is.SameAs(provider.Objects[1]));

        var constraints = SlippableMedia.CreateResizeConstraints(element);
        Assert.That(constraints.GetMaximumDuration(), Is.EqualTo(Seconds(6)));
        Assert.That(constraints.ClampLength(Seconds(8)), Is.EqualTo(Seconds(6)));
    }

    [Test]
    public void UnknownClearExpression_KeepsLocalMediaBounds()
    {
        SourceVideo local = CreateVideo(6);
        var portal = new PortalObject
        {
            Count = { Value = 0 },
            Clear = { Expression = Beutl.Engine.Expressions.Expression.Create<bool>("false") }
        };
        Element element = _harness.AddElement(MediaTime.Zero, Seconds(1));
        element.Objects.Add(local);
        element.Objects.Add(portal);

        var constraints = SlippableMedia.CreateResizeConstraints(element);
        Assert.That(constraints.GetMaximumDuration(), Is.EqualTo(Seconds(6)));
        Assert.That(constraints.ClampLength(Seconds(8)), Is.EqualTo(Seconds(6)));
    }

    [Test]
    public void LocalMediaBeforeThePortal_IsMappedThroughItsController()
    {
        AddProviders(1, Seconds(20));
        SourceVideo local = CreateVideo(6);
        Element element = AddPortal(MediaTime.Zero, Seconds(1), count: 1, local);
        element.Objects.Remove(local);
        element.Objects.Insert(0, local);
        element.Objects.Add(new DrawableTimeController { Speed = { Value = 200 } });

        var constraints = SlippableMedia.CreateResizeConstraints(element);
        Assert.That(constraints.GetMaximumDuration(), Is.EqualTo(Seconds(3)));
        Assert.That(constraints.ClampLength(Seconds(8)), Is.EqualTo(Seconds(3)));
    }

    private Element[] AddProviders(int count, MediaTime length)
    {
        var providers = new Element[count];
        for (int i = 0; i < count; i++)
        {
            Element provider = _harness.AddElement(MediaTime.Zero, length);
            provider.Objects.Add(CreateVideo((int)length.ToSeconds()));
            provider.ZIndex.Value = i + 1;
            providers[i] = provider;
        }

        return providers;
    }

    private Element AddPortal(MediaTime start, MediaTime length, int count, params EngineObject[] objects)
    {
        Element element = _harness.AddElement(start, length);
        element.Objects.Add(new PortalObject { Count = { Value = count } });
        foreach (EngineObject obj in objects) element.Objects.Add(obj);
        return element;
    }
}
