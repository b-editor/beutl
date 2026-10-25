using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transitions;
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

    [TestCase(0, false)]
    [TestCase(5, false)]
    [TestCase(-5, false)]
    [TestCase(5, true)]
    [TestCase(5, true, true)]
    [TestCase(-5, true, true)]
    public void EmptyPortal_MaximumDuration_IsUnboundedWithoutOverflow(double start, bool resizePeer, bool transition = false)
    {
        Element element = AddElement(start, 1, new PortalObject { Count = { Value = 10 } });
        Element peer = AddElement(start + 1, 2, new RectShape());
        peer.ZIndex.Value = 1;
        if (transition) peer.ExitTransition.Value = new CrossDissolveTransition { Duration = { Value = Seconds(1) } };

        var constraints = SlippableMedia.CreateResizeConstraints(element, resizePeer ? new HashSet<Element> { element, peer } : null);

        Assert.That(constraints.GetMaximumDuration(), Is.Null);
        Assert.That(constraints.ClampLength(Seconds(20)), Is.EqualTo(Seconds(20)));
    }

    [TestCase(nameof(SourceVideo), 1)]
    [TestCase(nameof(SourceVideo), 2)]
    [TestCase(nameof(SceneSound), 2)]
    public void ImportedMedia_DoesNotLimitPortalDuration(string mediaType, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Element provider = AddElement(i, 2);
            provider.ZIndex.Value = i + 1;
            AddMedia(provider, mediaType, speed: 100, durationSeconds: 1);
        }
        Element element = AddElement(0, 1, new PortalObject { Count = { Value = 10 } },
            new DrawableTimeController { Speed = { Value = 200 } });

        new ElementResizeService(_harness.History).Resize(_harness.Scene, [new(element, MediaTime.Zero, Seconds(8), 0)]);

        Assert.Multiple(() =>
        {
            Assert.That(element.Length.Value, Is.EqualTo(Seconds(8)));
            Assert.That(SlippableMedia.HasOriginalDuration(element), Is.False);
            Assert.That(SlippableMedia.GetOriginalDuration(element), Is.Null);
        });
    }

    [TestCase(2, 3)]
    [TestCase(4, 4)]
    [TestCase(8, 6)]
    public void SingleProvider_UsesTheLocalClockOnlyAfterItsEnd(double providerEnd, double maximum)
    {
        var imported = CreateVideo(6);
        AddElement(0, providerEnd, imported).ZIndex.Value = 1;
        var local = CreateVideo(6);
        Element element = AddElement(0, 8, new PortalObject { Count = { Value = 10 } }, local,
            new DrawableTimeController { Speed = { Value = 200 } });
        using var compositor = CreateCompositor(_harness.Scene);
        var resource = compositor.EvaluateGraphics(Seconds(0.5)).Objects.OfType<DrawableTimeController.Resource>().Single();
        Assert.That(resource.Target!.GetOriginal(), Is.SameAs(imported));
        if (providerEnd < 8)
        {
            resource = compositor.EvaluateGraphics(Seconds(providerEnd + 0.5)).Objects.OfType<DrawableTimeController.Resource>().Single();
            Assert.That(resource.Target!.GetOriginal(), Is.SameAs(local));
            var videoResource = (SourceVideo.Resource)resource.Target!;
            Assert.That((videoResource.RequestedPosition + videoResource.OffsetPosition).ToSeconds(),
                Is.EqualTo((providerEnd + 0.5) * 2).Within(0.000001));
        }
        element.Length.Value = Seconds(1);

        var constraints = SlippableMedia.CreateResizeConstraints(element);
        Assert.Multiple(() =>
        {
            Assert.That(constraints.GetMaximumDuration(), Is.EqualTo(Seconds(maximum)));
            Assert.That(constraints.ClampLength(Seconds(maximum)), Is.EqualTo(Seconds(maximum)), "Safe extension must remain available.");
            Assert.That(constraints.ClampLength(Seconds(8)), Is.EqualTo(Seconds(maximum)));
            Assert.That(SlippableMedia.HasOriginalDuration(element), Is.True);
            Assert.That(SlippableMedia.GetOriginalDuration(element), Is.EqualTo(Seconds(maximum)));
        });
        new ElementResizeService(_harness.History).Resize(_harness.Scene, [new(element, MediaTime.Zero, Seconds(8), 0)]);
        Assert.That(element.Length.Value, Is.EqualTo(Seconds(maximum)));
        Assert.That(local.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
        Assert.That(imported.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void IntermittentProviders_AllowSafeExtensionAcrossInputChanges(bool disabledController)
    {
        AddElement(0, 1, CreateVideo(20)).ZIndex.Value = 1;
        AddElement(2, 1, CreateVideo(20)).ZIndex.Value = 5;
        Element element = AddElement(0, 1.5, new PortalObject { Count = { Value = 10 } }, CreateVideo(6),
            new DrawableTimeController { Speed = { Value = 200 }, IsEnabled = { Value = !disabledController } });

        var constraints = SlippableMedia.CreateResizeConstraints(element);
        Assert.Multiple(() =>
        {
            Assert.That(constraints.GetMaximumDuration(), Is.EqualTo(Seconds(3)));
            Assert.That(constraints.ClampLength(Seconds(3)), Is.EqualTo(Seconds(3)));
            Assert.That(constraints.ClampLength(Seconds(6)), Is.EqualTo(Seconds(3)));
            Assert.That(SlippableMedia.GetOriginalDuration(element), Is.EqualTo(Seconds(3)));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnrelatedLocalMedia_KeepsItsOwnBounds(bool audio)
    {
        AddElement(0, 1, CreateVideo(20)).ZIndex.Value = 1;
        AddElement(2, 1, CreateVideo(20)).ZIndex.Value = 5;
        Element element = AddElement(0, 1, new PortalObject { Count = { Value = 10 } },
            new DrawableTimeController { Speed = { Value = 200 } });
        AddMedia(element, audio ? nameof(SceneSound) : nameof(SourceVideo), speed: 100, durationSeconds: 6);

        Assert.That(SlippableMedia.CreateResizeConstraints(element).ClampLength(Seconds(8)), Is.EqualTo(Seconds(6)));
        Assert.That(SlippableMedia.GetOriginalDuration(element), Is.EqualTo(Seconds(6)));
    }

    [TestCase(1, 1.5)]
    [TestCase(4, 3)]
    public void ChainedControllers_RebuildTheirInputOrderAtTheProviderBoundary(double providerEnd, double maximum)
    {
        AddElement(0, providerEnd, CreateVideo(20)).ZIndex.Value = 1;
        Element element = AddElement(0, 1, new PortalObject { Count = { Value = 10 } }, CreateVideo(6),
            new DrawableTimeController { Speed = { Value = 200 } }, new DrawableTimeController { Speed = { Value = 200 } });

        Assert.That(SlippableMedia.CreateResizeConstraints(element).ClampLength(Seconds(8)), Is.EqualTo(Seconds(maximum)));
        Assert.That(SlippableMedia.GetOriginalDuration(element), Is.EqualTo(Seconds(maximum)));
    }

    [Test]
    public void OverlappingProviders_KeepTheLocalVideoUncontrolledUntilAnInputEnds()
    {
        AddElement(0, 2, CreateVideo(20)).ZIndex.Value = 1;
        AddElement(0, 4, CreateVideo(20)).ZIndex.Value = 2;
        Element element = AddElement(0, 1, new PortalObject { Count = { Value = 10 } }, CreateVideo(6),
            new DrawableTimeController { Speed = { Value = 200 } }, new DrawableTimeController { Speed = { Value = 200 } });

        Assert.That(SlippableMedia.CreateResizeConstraints(element).ClampLength(Seconds(8)), Is.EqualTo(Seconds(3)));
    }

    [Test]
    public void PortalOnly_DisabledControllersDoNotImposeSourceBounds()
    {
        AddElement(0, 10, CreateVideo(1)).ZIndex.Value = 1;
        Element element = AddElement(0, 1, new PortalObject { Count = { Value = 10 } });
        for (int i = 0; i < 8; i++)
            element.Objects.Add(new DrawableTimeController { IsEnabled = { Value = false } });

        var constraints = SlippableMedia.CreateResizeConstraints(element);
        Assert.That(constraints.GetMaximumDuration(), Is.Null);
        Assert.That(constraints.ClampLength(Seconds(20)), Is.EqualTo(Seconds(20)));
    }

    [Test]
    public void LeftEdge_UsesProviderWindowsOnTheChangedElementClock()
    {
        AddElement(0, 4.5, CreateVideo(20)).ZIndex.Value = 1;
        var local = CreateVideo(6);
        Element element = AddElement(4, 1, new PortalObject { Count = { Value = 10 } }, local,
            new DrawableTimeController { Speed = { Value = 200 } });

        Assert.That(SlippableMedia.CreateResizeConstraints(element).ClampStart(Seconds(1)), Is.EqualTo(Seconds(2)));
        new ElementResizeService(_harness.History).Resize(_harness.Scene, [new(element, Seconds(1), Seconds(4), 0)]);
        Assert.That(element.Start.Value, Is.EqualTo(Seconds(2)));
        Assert.That(element.Length.Value, Is.EqualTo(Seconds(3)));
        Assert.That(local.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NestedLocalVideo_KeepsItsOwnControllerBounds(bool controlled)
    {
        AddElement(0, 2, CreateVideo(20)).ZIndex.Value = 1;
        var group = new DrawableGroup();
        group.Children.Add(CreateVideo(6));
        Element element = AddElement(0, 1, new PortalObject { Count = { Value = 10 } }, group);
        if (controlled) element.Objects.Add(new DrawableTimeController { Speed = { Value = 200 } });

        Assert.That(SlippableMedia.CreateResizeConstraints(element).ClampLength(Seconds(10)), Is.EqualTo(Seconds(controlled ? 3 : 6)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Transitions_UseOneInputForBothProviders(bool transition)
    {
        AddElement(0, 6, CreateVideo(20)).ZIndex.Value = 1;
        Element incoming = AddElement(2, 6, CreateVideo(20));
        incoming.ZIndex.Value = 1;
        incoming.EnterTransition.Value = new CrossDissolveTransition { Duration = { Value = MediaTime.Zero }, IsEnabled = { Value = transition } };
        var local = CreateVideo(6);
        Element element = AddElement(0, 8, new PortalObject { Count = { Value = 10 } }, local,
            new DrawableTimeController { Speed = { Value = 200 } }, new DrawableTimeController { Speed = { Value = 200 } });
        using var compositor = CreateCompositor(_harness.Scene);
        var controllers = compositor.EvaluateGraphics(Seconds(4)).Objects.OfType<DrawableTimeController.Resource>().ToArray();
        Assert.That(controllers, Has.Length.EqualTo(2));
        if (transition)
        {
            Assert.That(controllers[0].Target, Is.TypeOf<ClipTransitionPresenter.Resource>());
            Assert.That(controllers[1].Target!.GetOriginal(), Is.SameAs(local));
            Assert.That(((SourceVideo.Resource)controllers[1].Target!).RequestedPosition, Is.EqualTo(Seconds(8)));
        }
        element.Length.Value = Seconds(1);

        MediaTime maximum = Seconds(transition ? 3 : 6);
        var constraints = SlippableMedia.CreateResizeConstraints(element);
        Assert.That(constraints.GetMaximumDuration(), Is.EqualTo(maximum));
        Assert.That(constraints.ClampLength(Seconds(8)), Is.EqualTo(maximum));
        Assert.That(SlippableMedia.GetOriginalDuration(element), Is.EqualTo(maximum));
        new ElementResizeService(_harness.History).Resize(_harness.Scene, [new(element, MediaTime.Zero, maximum, 0)]);
        Assert.That(element.Length.Value, Is.EqualTo(maximum), "Safe extension must remain possible.");
    }

    [TestCase("enter", 4)]
    [TestCase("exit", 3)]
    [TestCase("disabled", 4)]
    public void Transitions_ChangeInputCountAtTheirOwnBoundaries(string edge, double maximum)
    {
        Element provider = AddElement(0, 4, new RectShape(), new EllipseShape());
        provider.ZIndex.Value = 1;
        var transition = new CrossDissolveTransition { Duration = { Value = Seconds(1) }, IsEnabled = { Value = edge != "disabled" } };
        if (edge == "enter") provider.EnterTransition.Value = transition;
        else provider.ExitTransition.Value = transition;
        Element element = AddElement(0, 1, new PortalObject { Count = { Value = 10 } }, CreateVideo(6),
            new DrawableTimeController { Speed = { Value = 200 } }, new DrawableTimeController { Speed = { Value = 200 } });

        var constraints = SlippableMedia.CreateResizeConstraints(element);
        Assert.That(constraints.ClampLength(Seconds(6)), Is.EqualTo(Seconds(maximum)));
        Assert.That(constraints.GetMaximumDuration(), Is.EqualTo(Seconds(maximum)));
    }

    [Test]
    public void Transitions_KeepTheOutgoingProviderHiddenAfterHandover()
    {
        AddElement(0, 5, new RectShape()).ZIndex.Value = 1;
        Element incoming = AddElement(2, 4, new EllipseShape());
        incoming.ZIndex.Value = 1;
        incoming.EnterTransition.Value = new CrossDissolveTransition { Duration = { Value = Seconds(0.5) } };
        incoming.ExitTransition.Value = new CrossDissolveTransition { Duration = { Value = Seconds(0.5) } };
        var local = CreateVideo(9);
        Element element = AddElement(0, 6, new PortalObject { Count = { Value = 10 } }, local,
            new DrawableTimeController { Speed = { Value = 200 } }, new DrawableTimeController { Speed = { Value = 200 } });
        using var compositor = CreateCompositor(_harness.Scene);
        var controllers = compositor.EvaluateGraphics(Seconds(4.25)).Objects.OfType<DrawableTimeController.Resource>().ToArray();
        Assert.That(controllers[0].Target!.GetOriginal(), Is.SameAs(incoming.Objects[0]));
        Assert.That(controllers[1].Target!.GetOriginal(), Is.SameAs(local));
        element.Length.Value = Seconds(1);

        Assert.That(SlippableMedia.CreateResizeConstraints(element).ClampLength(Seconds(6)), Is.EqualTo(Seconds(4.5)));
    }

    [Test]
    public void Transitions_CoverTheToleratedGapBetweenProviders()
    {
        Element outgoing = AddElement(0, 2, new RectShape(), new EllipseShape());
        outgoing.ZIndex.Value = 1;
        outgoing.Length.Value -= MediaTime.FromTimeSpan(TimeSpan.FromTicks(1));
        Element incoming = AddElement(2, 2, new RectShape(), new EllipseShape());
        incoming.ZIndex.Value = 1;
        incoming.EnterTransition.Value = new CrossDissolveTransition { Duration = { Value = Seconds(0.5) } };
        Element element = AddElement(0, 1, new PortalObject { Count = { Value = 10 } }, CreateVideo(6),
            new DrawableTimeController { Speed = { Value = 200 } }, new DrawableTimeController { Speed = { Value = 200 } });

        Assert.That(SlippableMedia.CreateResizeConstraints(element).ClampLength(Seconds(4)), Is.EqualTo(Seconds(4)));
    }

    [TestCase(false, 3)]
    [TestCase(true, 6)]
    public void Transitions_UseTheResizedProvidersBoundary(bool resizePeer, double maximum)
    {
        Element provider = AddElement(0, 4, new RectShape(), new EllipseShape());
        provider.ZIndex.Value = 1;
        provider.ExitTransition.Value = new CrossDissolveTransition { Duration = { Value = Seconds(1) } };
        Element element = AddElement(0, 1, new PortalObject { Count = { Value = 10 } }, CreateVideo(6),
            new DrawableTimeController { Speed = { Value = 200 } }, new DrawableTimeController { Speed = { Value = 200 } });

        var constraints = SlippableMedia.CreateResizeConstraints(element, resizePeer ? new HashSet<Element> { element, provider } : null);
        Assert.That(constraints.ClampLength(Seconds(8)), Is.EqualTo(Seconds(maximum)));
        Assert.That(constraints.GetMaximumDuration(), Is.EqualTo(Seconds(maximum)));
        Assert.That(provider.Length.Value, Is.EqualTo(Seconds(4)), "Probing geometry must not mutate the provider.");
    }

    [TestCase("disabled")]
    [TestCase("outside")]
    [TestCase("audio")]
    [TestCase("muted")]
    public void UnrelatedClips_DoNotMultiplyFlowSnapshots(string kind)
    {
        AddElement(0, 4, new RectShape()).ZIndex.Value = 1;
        Element element = AddElement(0, 1, new PortalObject { Count = { Value = 10 } }, CreateVideo(6),
            new DrawableTimeController { Speed = { Value = 200 } });
        var baseline = SlippableMedia.CreateResizeConstraints(element);
        MediaTime? maximum = baseline.GetMaximumDuration();
        int mappings = baseline.MediaTargets.Count;
        if (kind == "muted") _harness.AddLayer(5).IsVideoMuted.Value = true;
        for (int i = 0; i < 100; i++)
        {
            Element other = AddElement(i / 50.0, 0.005, kind == "audio" ? new SceneSound() : new RectShape());
            other.ZIndex.Value = kind == "outside" ? 20 : kind == "muted" ? 5 : 1;
            other.IsEnabled.Value = kind != "disabled";
        }

        var constraints = SlippableMedia.CreateResizeConstraints(element);
        Assert.That(constraints.GetMaximumDuration(), Is.EqualTo(maximum));
        Assert.That(constraints.MediaTargets, Has.Count.EqualTo(mappings), "Unrelated clip edges must not create additional flow mappings.");
    }

    [TestCase(false, "false", 6d)]
    [TestCase(true, "false", 6d)]
    [TestCase(true, "1 == 2", 6d)]
    [TestCase(false, "true", null)]
    [TestCase(true, "true", null)]
    [TestCase(false, "Time < 3", 6d)]
    public void ClearExpression_PreservesTheLocalMediaPath(bool storedClear, string expression, double? maximum)
    {
        SourceVideo local = CreateVideo(6);
        var portal = new PortalObject { Count = { Value = 0 }, Clear = { Value = storedClear, Expression = Expression.Create<bool>(expression) } };
        Element element = AddElement(0, 8, local, portal);
        using var compositor = CreateCompositor(_harness.Scene);
        var frame = compositor.EvaluateGraphics(Seconds(7));
        Assert.That(frame.Objects.OfType<SourceVideo.Resource>().Any(resource => resource.GetOriginal() == local), Is.EqualTo(maximum.HasValue));
        element.Length.Value = Seconds(1);

        var constraints = SlippableMedia.CreateResizeConstraints(element);
        Assert.That(constraints.IsSupported, Is.True);
        Assert.That(constraints.GetMaximumDuration(), Is.EqualTo(maximum.HasValue ? Seconds(maximum.Value) : (MediaTime?)null));
        Assert.That(constraints.ClampLength(Seconds(8)), Is.EqualTo(Seconds(maximum ?? 8)));
        Assert.That(SlippableMedia.GetOriginalDuration(element), Is.EqualTo(maximum.HasValue ? Seconds(maximum.Value) : (MediaTime?)null));
        new ElementResizeService(_harness.History).Resize(_harness.Scene, [new(element, MediaTime.Zero, Seconds(8), 0)]);
        Assert.That(element.Length.Value, Is.EqualTo(Seconds(maximum ?? 8)));
        Assert.That(local.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
    }

    [TestCase("false", 3)]
    [TestCase("true", 2)]
    [TestCase("Time >= 1", 2)]
    public void ClearExpression_PreservesBothPossibleControllerInputs(string expression, double maximum)
    {
        SourceVideo before = CreateVideo(6);
        SourceVideo after = CreateVideo(4);
        var portal = new PortalObject { Clear = { Expression = Expression.Create<bool>(expression) } };
        Element element = AddElement(0, 8, before, portal, after, new DrawableTimeController { Speed = { Value = 200 } });
        using var compositor = CreateCompositor(_harness.Scene);
        foreach (double time in new[] { 0.5, 1.5 })
        {
            var controller = compositor.EvaluateGraphics(Seconds(time)).Objects.OfType<DrawableTimeController.Resource>().Single();
            bool cleared = expression == "true" || expression == "Time >= 1" && time >= 1;
            Assert.That(controller.Target!.GetOriginal(), Is.SameAs(cleared ? after : before));
        }
        element.Length.Value = Seconds(1);

        var constraints = SlippableMedia.CreateResizeConstraints(element);
        Assert.That(constraints.IsSupported, Is.True);
        Assert.That(constraints.GetMaximumDuration(), Is.EqualTo(Seconds(maximum)));
        Assert.That(constraints.ClampLength(Seconds(maximum)), Is.EqualTo(Seconds(maximum)), "Safe extension must remain possible.");
        Assert.That(constraints.ClampLength(Seconds(8)), Is.EqualTo(Seconds(maximum)));
    }

    [Test]
    public void ClearExpression_PortalOnly_RemainsUnbounded()
    {
        AddElement(0, 8, CreateVideo(1)).ZIndex.Value = 1;
        Element element = AddElement(0, 1, new PortalObject { Count = { Value = 10 }, Clear = { Expression = Expression.Create<bool>("Time > 1") } },
            new DrawableTimeController { Speed = { Value = 200 } });

        var constraints = SlippableMedia.CreateResizeConstraints(element);
        Assert.That(constraints.GetMaximumDuration(), Is.Null);
        Assert.That(constraints.ClampLength(Seconds(20)), Is.EqualTo(Seconds(20)));
    }

    [Test]
    public void ClearExpression_MultiplePaths_DoNotCountADisabledControllerMoreThanOnce()
    {
        Element element = AddElement(0, 1, CreateVideo(6));
        for (int i = 0; i < 8; i++)
            element.Objects.Add(new PortalObject { Clear = { Expression = Expression.Create<bool>("Time > 1") } });
        element.Objects.Add(CreateVideo(6));
        element.Objects.Add(new DrawableTimeController { Speed = { Value = 200 }, IsEnabled = { Value = false } });

        var constraints = SlippableMedia.CreateResizeConstraints(element);
        Assert.That(constraints.IsSupported, Is.True);
        Assert.That(constraints.GetMaximumDuration(), Is.EqualTo(Seconds(3)));
        Assert.That(constraints.ClampLength(Seconds(3)), Is.EqualTo(Seconds(3)));
    }

    private Element AddElement(double start, double length, params EngineObject[] objects)
    {
        Element element = _harness.AddElement(Seconds(start), Seconds(length));
        foreach (EngineObject obj in objects) element.Objects.Add(obj);
        return element;
    }
}
