using System.Text.Json.Nodes;
using Beutl.Animation.Easings;
using Beutl.Composition;
using Beutl.Editor.Services;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transitions;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public class ElementTransitionCompositionTests
{
    [OneTimeSetUp]
    public void RegisterDecoder() => TestMediaHelper.RegisterTestDecoder();

    [Test]
    public void BothSides_CentreTheTransitionOnTheCut()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_centred", duration: Seconds(10));
        Cut cut = AddCut(harness);
        cut.Outgoing.ExitTransition = Transition(0.5);
        cut.Incoming.EnterTransition = Transition(0.5);
        using var compositor = new SceneCompositor(harness.Scene);

        ClipTransitionPresenter.Resource before = SinglePresenter(compositor.EvaluateGraphics(Seconds(1.75)));
        float progressBefore = before.Progress;
        ClipTransitionPresenter.Resource after = SinglePresenter(compositor.EvaluateGraphics(Seconds(2.25)));

        Assert.Multiple(() =>
        {
            Assert.That(after, Is.SameAs(before), "one presenter draws the whole boundary");
            Assert.That(progressBefore, Is.EqualTo(0.25f).Within(1e-6));
            Assert.That(after.Progress, Is.EqualTo(0.75f).Within(1e-6));
            Assert.That(after.From.Select(r => r.GetOriginal()), Is.EqualTo(new Drawable[] { cut.OutgoingShape }));
            Assert.That(after.To.Select(r => r.GetOriginal()), Is.EqualTo(new Drawable[] { cut.IncomingShape }));
            Assert.That(after.RequireOriginal().ZIndex, Is.EqualTo(cut.Incoming.ZIndex));
            Assert.That(Originals(compositor.EvaluateGraphics(Seconds(1.4))), Is.EqualTo(new Drawable[] { cut.OutgoingShape }));
            Assert.That(Originals(compositor.EvaluateGraphics(Seconds(2.5))), Is.EqualTo(new Drawable[] { cut.IncomingShape }));
        });
    }

    [TestCase("exit", 1.0, 2.0)]
    [TestCase("enter", 2.0, 3.0)]
    public void OneSide_PlacesTheTransitionAheadOfOrBehindTheCut(string side, double start, double end)
    {
        using var harness = new SceneHistoryHarness("beutl_transition_one_side", duration: Seconds(10));
        Cut cut = AddCut(harness);
        if (side == "exit")
            cut.Outgoing.ExitTransition = Transition(1);
        else
            cut.Incoming.EnterTransition = Transition(1);
        using var compositor = new SceneCompositor(harness.Scene);

        TransitionBoundary boundary = ElementTransitions.GetBoundaryAtStart(cut.Incoming)!.Value;
        ClipTransitionPresenter.Resource presenter = SinglePresenter(compositor.EvaluateGraphics(Seconds((start + end) / 2)));

        Assert.Multiple(() =>
        {
            Assert.That(boundary.Region, Is.EqualTo(TimeRange.FromRange(Seconds(start), Seconds(end))));
            Assert.That(ElementTransitions.GetBoundaryAtEnd(cut.Outgoing), Is.EqualTo(boundary));
            Assert.That(presenter.Progress, Is.EqualTo(0.5f).Within(1e-6));
            Assert.That(presenter.From, Has.Count.EqualTo(1));
            Assert.That(presenter.To, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void APreviewedDuration_ResizesTheSpanWithoutEditingTheTransition()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_preview", duration: Seconds(10));
        Cut cut = AddCut(harness);
        cut.Outgoing.ExitTransition = Transition(0.25);
        ClipTransition enter = Transition(0.25);
        cut.Incoming.EnterTransition = enter;
        var preview = new TransitionDurationOverride(enter, Seconds(0.5));

        TimeRange? fromIncoming = ElementTransitions.GetBoundaryAtStart(cut.Incoming, preview)?.Region;
        TimeRange? fromOutgoing = ElementTransitions.GetBoundaryAtEnd(cut.Outgoing, preview)?.Region;

        Assert.Multiple(() =>
        {
            Assert.That(fromIncoming, Is.EqualTo(TimeRange.FromRange(Seconds(1.75), Seconds(2.5))));
            Assert.That(fromOutgoing, Is.EqualTo(fromIncoming), "both elements see the same previewed span");
            Assert.That(enter.Duration.CurrentValue, Is.EqualTo(Seconds(0.25)));
        });
    }

    [Test]
    public void TheIncomingSide_DecidesHowTheBoundaryBlends()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_decides", duration: Seconds(10));
        Cut cut = AddCut(harness);
        ClipTransition exit = new WipeTransition { Duration = { CurrentValue = Seconds(0.5) } };
        ClipTransition enter = new FadeTransition { Duration = { CurrentValue = Seconds(0.5) } };
        cut.Outgoing.ExitTransition = exit;
        cut.Incoming.EnterTransition = enter;
        using var compositor = new SceneCompositor(harness.Scene);

        object? withEnter = SinglePresenter(compositor.EvaluateGraphics(Seconds(2))).TransitionResource?.GetOriginal();
        cut.Incoming.EnterTransition = null;
        object? withoutEnter = SinglePresenter(compositor.EvaluateGraphics(Seconds(1.75))).TransitionResource?.GetOriginal();

        Assert.Multiple(() =>
        {
            Assert.That(withEnter, Is.SameAs(enter));
            Assert.That(withoutEnter, Is.SameAs(exit));
        });
    }

    [TestCase("gap")]
    [TestCase("disabled")]
    [TestCase("other layer")]
    public void WithoutAnElementAcrossTheEdge_TheTransitionFadesFromNothing(string kind)
    {
        using var harness = new SceneHistoryHarness("beutl_transition_alone", duration: Seconds(10));
        Cut cut = AddCut(harness);
        switch (kind)
        {
            case "gap":
                cut.Outgoing.Length = Seconds(1.5);
                break;
            case "disabled":
                cut.Outgoing.IsEnabled = false;
                break;
            default:
                cut.Outgoing.ZIndex = 1;
                break;
        }

        cut.Incoming.EnterTransition = Transition(1);
        using var compositor = new SceneCompositor(harness.Scene);

        ClipTransitionPresenter.Resource presenter = SinglePresenter(compositor.EvaluateGraphics(Seconds(2.5)));

        Assert.Multiple(() =>
        {
            Assert.That(presenter.From, Is.Empty);
            Assert.That(presenter.To.Select(r => r.GetOriginal()), Is.EqualTo(new Drawable[] { cut.IncomingShape }));
        });
    }

    [Test]
    public void AnExitWithNothingAfterIt_FadesToNothing()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_fade_out", duration: Seconds(10));
        Cut cut = AddCut(harness);
        cut.Incoming.ExitTransition = Transition(1);
        using var compositor = new SceneCompositor(harness.Scene);

        ClipTransitionPresenter.Resource presenter = SinglePresenter(compositor.EvaluateGraphics(Seconds(3.5)));

        Assert.Multiple(() =>
        {
            Assert.That(presenter.From.Select(r => r.GetOriginal()), Is.EqualTo(new Drawable[] { cut.IncomingShape }));
            Assert.That(presenter.To, Is.Empty);
            Assert.That(presenter.Progress, Is.EqualTo(0.5f).Within(1e-6));
        });
    }

    [Test]
    public void AnOverlappingBoundary_BlendsAcrossTheOverlap()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_overlap", duration: Seconds(10));
        Cut cut = AddCut(harness);
        cut.Outgoing.Length = Seconds(2.5);
        cut.Incoming.EnterTransition = Transition(0);
        using var compositor = new SceneCompositor(harness.Scene);

        ClipTransitionPresenter.Resource presenter = SinglePresenter(compositor.EvaluateGraphics(Seconds(2.25)));

        Assert.Multiple(() =>
        {
            Assert.That(
                ElementTransitions.GetBoundaryAtStart(cut.Incoming)?.Region,
                Is.EqualTo(TimeRange.FromRange(Seconds(2), Seconds(2.5))));
            Assert.That(presenter.Progress, Is.EqualTo(0.5f).Within(1e-6));
            Assert.That(presenter.From.Select(r => r.GetOriginal()), Is.EqualTo(new Drawable[] { cut.OutgoingShape }));
            Assert.That(presenter.To.Select(r => r.GetOriginal()), Is.EqualTo(new Drawable[] { cut.IncomingShape }));
        });
    }

    // The outgoing end is a tick past the incoming start: still one boundary, drawn once.
    [Test]
    public void ElementsTouchingWithinTheTolerance_AreDrawnOnlyByTheirTransition()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_tick", duration: Seconds(10));
        Cut cut = AddCut(harness);
        cut.Outgoing.Length = Seconds(2) + TimeSpan.FromTicks(1);
        cut.Incoming.EnterTransition = Transition(1);
        using var compositor = new SceneCompositor(harness.Scene);

        CompositionFrame frame = compositor.EvaluateGraphics(Seconds(2));

        Assert.That(SinglePresenter(frame).From.Select(r => r.GetOriginal()), Is.EqualTo(new Drawable[] { cut.OutgoingShape }));
    }

    // The tolerance also accepts a gap: a frame inside it belongs to neither element's own range but is
    // still inside their transition.
    [Test]
    public void AFrameInsideAToleratedGap_IsDrawnByTheTransition()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_gap", duration: Seconds(10));
        Cut cut = AddCut(harness);
        cut.Outgoing.Length = Seconds(2) - TimeSpan.FromTicks(1);
        cut.Incoming.EnterTransition = Transition(1);
        using var compositor = new SceneCompositor(harness.Scene);

        CompositionFrame frame = compositor.EvaluateGraphics(Seconds(2) - TimeSpan.FromTicks(1));

        Assert.That(SinglePresenter(frame).From.Select(r => r.GetOriginal()), Is.EqualTo(new Drawable[] { cut.OutgoingShape }));
    }

    // Overlapping elements whose span stops at the incoming element's middle: once the transition has
    // handed over, the outgoing element must not show again for the rest of its range.
    [Test]
    public void AnOutgoingElementThatRunsOnPastItsTransition_StaysHidden()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_handed_over", duration: Seconds(10));
        var outgoingShape = new RectShape();
        Element outgoing = harness.AddElement(TimeSpan.Zero, Seconds(5));
        outgoing.Objects.Add(outgoingShape);
        var incomingShape = new EllipseShape();
        Element incoming = harness.AddElement(Seconds(2), Seconds(4));
        incoming.Objects.Add(incomingShape);
        incoming.EnterTransition = Transition(0.5);
        incoming.ExitTransition = Transition(0.5);
        using var compositor = new SceneCompositor(harness.Scene);

        TimeRange? region = ElementTransitions.GetBoundaryAtStart(incoming)?.Region;
        CompositionFrame after = compositor.EvaluateGraphics(Seconds(4.5));

        Assert.Multiple(() =>
        {
            Assert.That(region, Is.EqualTo(TimeRange.FromRange(Seconds(2), Seconds(4))));
            Assert.That(Originals(after), Is.EqualTo(new Drawable[] { incomingShape }));
        });
    }

    [Test]
    public void TheParticipants_AreTheElementsOnEitherSideOfAnActiveTransition()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_participants", duration: Seconds(10));
        Cut cut = AddCut(harness);
        Element unrelated = harness.AddElement(Seconds(6), Seconds(1));
        HashSet<Element> without = ElementTransitions.GetParticipants(harness.Scene);
        cut.Outgoing.ExitTransition = Transition(0.5);

        HashSet<Element> with = ElementTransitions.GetParticipants(harness.Scene);

        Assert.Multiple(() =>
        {
            Assert.That(without, Is.Empty);
            Assert.That(with, Is.EquivalentTo(new[] { cut.Outgoing, cut.Incoming }));
            Assert.That(with, Does.Not.Contain(unrelated));
        });
    }

    // An extension's transition may animate its properties, which evaluate against the owner's range.
    [Test]
    public void ATransition_FollowsTheRangeOfItsElement()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_range", duration: Seconds(10));
        Cut cut = AddCut(harness);
        ClipTransition transition = Transition(0.5);

        cut.Incoming.EnterTransition = transition;
        TimeRange assigned = transition.TimeRange;
        cut.Incoming.Start = Seconds(3);

        Assert.Multiple(() =>
        {
            Assert.That(assigned, Is.EqualTo(new TimeRange(Seconds(2), Seconds(2))));
            Assert.That(transition.TimeRange, Is.EqualTo(new TimeRange(Seconds(3), Seconds(2))));
            Assert.That(transition.IsTimeAnchor, Is.False, "the range is not saved with the transition");
        });
    }

    // A=[0,10] meets both B=[5,20] and C=[6,15], and C's only candidate before it is A, but A pairs with
    // B, which starts first. An exit transition on A must make one boundary, not one with each.
    [Test]
    public void AnElementMetByTwoOthers_PairsWithOnlyOne()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_reciprocal", duration: Seconds(30));
        var aShape = new RectShape();
        Element a = harness.AddElement(TimeSpan.Zero, Seconds(10));
        a.Objects.Add(aShape);
        Element b = harness.AddElement(Seconds(5), Seconds(15));
        b.Objects.Add(new EllipseShape());
        Element c = harness.AddElement(Seconds(6), Seconds(9));
        c.Objects.Add(new RectShape());
        a.ExitTransition = Transition(1);
        using var compositor = new SceneCompositor(harness.Scene);

        CompositionFrame frame = compositor.EvaluateGraphics(Seconds(7));
        int presenters = frame.Objects.Count(r => r is ClipTransitionPresenter.Resource);

        Assert.Multiple(() =>
        {
            Assert.That(ElementTransitions.GetBoundaryAtEnd(a)?.Incoming, Is.SameAs(b));
            Assert.That(ElementTransitions.GetBoundaryAtStart(b)?.Outgoing, Is.SameAs(a));
            Assert.That(ElementTransitions.GetBoundaryAtStart(c), Is.Null);
            Assert.That(presenters, Is.EqualTo(1), "A is drawn by one transition");
        });
    }

    // Both sides draw the layer above them through a portal; the outgoing side taking that layer must not
    // leave the incoming side without it, nor let it draw a second time on its own.
    [Test]
    public void BothSidesOfATransition_ReachTheLayerTheirPortalsDraw()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_portals", duration: Seconds(10));
        Element outgoing = harness.AddElement(TimeSpan.Zero, Seconds(2));
        outgoing.Objects.Add(new PortalObject { Count = { CurrentValue = 1 } });
        Element incoming = harness.AddElement(Seconds(2), Seconds(2));
        incoming.Objects.Add(new PortalObject { Count = { CurrentValue = 1 } });
        var providerShape = new RectShape();
        Element provider = harness.AddElement(TimeSpan.Zero, Seconds(4), zIndex: 1);
        provider.Objects.Add(providerShape);
        incoming.EnterTransition = Transition(1);
        using var compositor = new SceneCompositor(harness.Scene);

        CompositionFrame frame = compositor.EvaluateGraphics(Seconds(2.25));
        ClipTransitionPresenter.Resource presenter = SinglePresenter(frame);

        Assert.Multiple(() =>
        {
            Assert.That(presenter.From.Select(r => r.GetOriginal()), Does.Contain(providerShape));
            Assert.That(presenter.To.Select(r => r.GetOriginal()), Does.Contain(providerShape));
        });
    }

    // Before the cut the incoming side holds its first frame, but the layer its portal draws has not
    // ended, so on both sides it plays on at the frame's time instead of jumping to that held time.
    [Test]
    public void ALayerAPortalDrawsIntoATransition_PlaysOnAtTheFramesTime()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_portal_time", duration: Seconds(10));
        Element outgoing = harness.AddElement(TimeSpan.Zero, Seconds(2));
        outgoing.Objects.Add(new PortalObject { Count = { CurrentValue = 1 } });
        Element incoming = harness.AddElement(Seconds(2), Seconds(2));
        incoming.Objects.Add(new PortalObject { Count = { CurrentValue = 1 } });
        Element provider = harness.AddElement(TimeSpan.Zero, Seconds(4), zIndex: 1);
        provider.Objects.Add(CreateVideo());
        outgoing.ExitTransition = Transition(0.5);
        incoming.EnterTransition = Transition(0.5);
        using var compositor = new SceneCompositor(harness.Scene) { ForceOriginalSource = true };

        ClipTransitionPresenter.Resource presenter = SinglePresenter(compositor.EvaluateGraphics(Seconds(1.75)));
        double fromPosition = presenter.From.OfType<SourceVideo.Resource>().Single().RequestedPosition.TotalSeconds;
        double toPosition = presenter.To.OfType<SourceVideo.Resource>().Single().RequestedPosition.TotalSeconds;

        Assert.Multiple(() =>
        {
            Assert.That(fromPosition, Is.EqualTo(1.75).Within(1e-6));
            Assert.That(toPosition, Is.EqualTo(1.75).Within(1e-6));
        });
    }

    [Test]
    public void EachSide_HoldsItsEdgeFrameOutsideItsOwnRange()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_hold", duration: Seconds(10));
        Element outgoing = harness.AddElement(TimeSpan.Zero, Seconds(2));
        outgoing.Objects.Add(CreateVideo());
        Element incoming = harness.AddElement(Seconds(2), Seconds(2));
        incoming.Objects.Add(CreateVideo());
        outgoing.ExitTransition = Transition(0.5);
        incoming.EnterTransition = Transition(0.5);
        using var compositor = new SceneCompositor(harness.Scene) { ForceOriginalSource = true };

        ClipTransitionPresenter.Resource early = SinglePresenter(compositor.EvaluateGraphics(Seconds(1.75)));
        double outgoingEarly = ((SourceVideo.Resource)early.From.Single()).RequestedPosition.TotalSeconds;
        double incomingEarly = ((SourceVideo.Resource)early.To.Single()).RequestedPosition.TotalSeconds;
        ClipTransitionPresenter.Resource late = SinglePresenter(compositor.EvaluateGraphics(Seconds(2.25)));
        double outgoingLate = ((SourceVideo.Resource)late.From.Single()).RequestedPosition.TotalSeconds;
        double incomingLate = ((SourceVideo.Resource)late.To.Single()).RequestedPosition.TotalSeconds;

        Assert.Multiple(() =>
        {
            Assert.That(outgoingEarly, Is.EqualTo(1.75).Within(1e-6));
            Assert.That(incomingEarly, Is.Zero, "the incoming clip holds its first frame before its start");
            Assert.That(outgoingLate, Is.EqualTo(2 - 1.0 / 30).Within(1e-6), "the outgoing clip holds its last frame");
            Assert.That(incomingLate, Is.EqualTo(0.25).Within(1e-6));
        });
    }

    [Test]
    public void AnElementWithTransitionsAtBothEnds_SplitsItsLengthBetweenThem()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_both_ends", duration: Seconds(10));
        Cut cut = AddCut(harness);
        cut.Incoming.Length = Seconds(1);
        var third = new RectShape();
        Element next = harness.AddElement(Seconds(3), Seconds(2));
        next.Objects.Add(third);
        cut.Incoming.EnterTransition = Transition(5);
        cut.Incoming.ExitTransition = Transition(5);
        using var compositor = new SceneCompositor(harness.Scene);

        TimeRange start = ElementTransitions.GetBoundaryAtStart(cut.Incoming)!.Value.Region;
        TimeRange end = ElementTransitions.GetBoundaryAtEnd(cut.Incoming)!.Value.Region;
        ClipTransitionPresenter.Resource first = SinglePresenter(compositor.EvaluateGraphics(Seconds(2.25)));
        ClipTransitionPresenter.Resource second = SinglePresenter(compositor.EvaluateGraphics(Seconds(2.75)));

        Assert.Multiple(() =>
        {
            Assert.That(start, Is.EqualTo(TimeRange.FromRange(Seconds(2), Seconds(2.5))));
            Assert.That(end, Is.EqualTo(TimeRange.FromRange(Seconds(2.5), Seconds(3))));
            Assert.That(first.To.Select(r => r.GetOriginal()), Is.EqualTo(new Drawable[] { cut.IncomingShape }));
            Assert.That(second.From.Select(r => r.GetOriginal()), Is.EqualTo(new Drawable[] { cut.IncomingShape }));
            Assert.That(second.To.Select(r => r.GetOriginal()), Is.EqualTo(new Drawable[] { third }));
        });
    }

    [Test]
    public void TheEasing_ShapesTheProgress()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_easing", duration: Seconds(10));
        Cut cut = AddCut(harness);
        ClipTransition transition = Transition(1);
        transition.Easing.CurrentValue = new CubicEaseIn();
        cut.Incoming.EnterTransition = transition;
        using var compositor = new SceneCompositor(harness.Scene);

        ClipTransitionPresenter.Resource presenter = SinglePresenter(compositor.EvaluateGraphics(Seconds(2.5)));

        Assert.That(presenter.Progress, Is.EqualTo(0.125f).Within(1e-6));
    }

    // An expression on the easing is evaluated for the frame, so the curve it gives is the one drawn.
    [Test]
    public void AnEasingExpression_ShapesTheProgress()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_easing_expression", duration: Seconds(10));
        Cut cut = AddCut(harness);
        ClipTransition transition = Transition(1);
        transition.Easing.Expression = new ConstantExpression<Easing>(new CubicEaseIn());
        cut.Incoming.EnterTransition = transition;
        using var compositor = new SceneCompositor(harness.Scene);

        ClipTransitionPresenter.Resource presenter = SinglePresenter(compositor.EvaluateGraphics(Seconds(2.5)));

        Assert.That(presenter.Progress, Is.EqualTo(0.125f).Within(1e-6));
    }

    // The compositor keeps the elements that can take part in a transition between frames; adding one
    // must still reach the next frame.
    [Test]
    public void ATransitionAddedBetweenFrames_ReachesTheNextFrame()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_added_later", duration: Seconds(10));
        Cut cut = AddCut(harness);
        cut.Outgoing.Length = Seconds(2) + TimeSpan.FromTicks(1);
        using var compositor = new SceneCompositor(harness.Scene);
        int before = compositor.EvaluateGraphics(Seconds(2)).Objects.Length;

        cut.Incoming.EnterTransition = Transition(1);
        CompositionFrame after = compositor.EvaluateGraphics(Seconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(2), "without a transition both touching elements draw");
            Assert.That(SinglePresenter(after).From.Select(r => r.GetOriginal()), Is.EqualTo(new Drawable[] { cut.OutgoingShape }));
        });
    }

    [Test]
    public void ADisabledTransition_IsACut()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_disabled", duration: Seconds(10));
        Cut cut = AddCut(harness);
        cut.Incoming.EnterTransition = new CrossDissolveTransition { IsEnabled = false };
        using var compositor = new SceneCompositor(harness.Scene);

        Assert.That(Originals(compositor.EvaluateGraphics(Seconds(2.25))), Is.EqualTo(new Drawable[] { cut.IncomingShape }));
    }

    [Test]
    public void ThePresenterVersion_MovesWithProgressAndContent()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_version", duration: Seconds(10));
        Cut cut = AddCut(harness);
        cut.Incoming.EnterTransition = Transition(1);
        using var compositor = new SceneCompositor(harness.Scene);

        ClipTransitionPresenter.Resource presenter = SinglePresenter(compositor.EvaluateGraphics(Seconds(2.25)));
        int first = presenter.Version;
        SinglePresenter(compositor.EvaluateGraphics(Seconds(2.25)));
        int unchanged = presenter.Version;
        SinglePresenter(compositor.EvaluateGraphics(Seconds(2.5)));
        int progressed = presenter.Version;
        cut.IncomingShape.Width.CurrentValue = 42;
        SinglePresenter(compositor.EvaluateGraphics(Seconds(2.5)));
        int edited = presenter.Version;

        Assert.Multiple(() =>
        {
            Assert.That(unchanged, Is.EqualTo(first));
            Assert.That(progressed, Is.Not.EqualTo(unchanged));
            Assert.That(edited, Is.Not.EqualTo(progressed));
        });
    }

    [Test]
    public void RollingTheCut_CarriesTheTransitionWithIt()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_roll", duration: Seconds(10));
        Cut cut = AddCut(harness);
        cut.Outgoing.ExitTransition = Transition(0.25);
        cut.Incoming.EnterTransition = Transition(0.25);
        harness.History.Commit("transition");
        var service = new ElementResizeService(harness.History);

        bool rolled = service.Roll(harness.Scene, [new ElementTrimPair(cut.Outgoing, cut.Incoming)], Seconds(0.5));

        Assert.Multiple(() =>
        {
            Assert.That(rolled, Is.True);
            Assert.That(
                ElementTransitions.GetBoundaryAtStart(cut.Incoming)?.Region,
                Is.EqualTo(TimeRange.FromRange(Seconds(2.25), Seconds(2.75))));
        });
    }

    [Test]
    public void TheTransitions_AreHierarchicalChildrenOfTheirElement()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_children", duration: Seconds(10));
        Cut cut = AddCut(harness);
        ClipTransition enter = Transition(1);
        ClipTransition exit = Transition(1);

        cut.Incoming.EnterTransition = enter;
        cut.Incoming.ExitTransition = exit;

        Assert.Multiple(() =>
        {
            Assert.That(enter.HierarchicalParent, Is.SameAs(cut.Incoming));
            Assert.That(exit.HierarchicalParent, Is.SameAs(cut.Incoming));
            Assert.That(enter.IsTimeAnchor, Is.False, "a transition must not persist a time range under its Duration key");
        });
    }

    [Test]
    public void EditingATransition_RaisesTheElementsEditedEvent()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_edited", duration: Seconds(10));
        Cut cut = AddCut(harness);
        ClipTransition transition = Transition(1);
        int edits = 0;
        cut.Incoming.Edited += (_, _) => edits++;

        cut.Incoming.ExitTransition = transition;
        int afterAssign = edits;
        transition.Duration.CurrentValue = Seconds(0.5);
        int afterEdit = edits;
        cut.Incoming.ExitTransition = null;
        transition.Duration.CurrentValue = Seconds(0.25);

        Assert.Multiple(() =>
        {
            Assert.That(afterAssign, Is.GreaterThan(0));
            Assert.That(afterEdit, Is.GreaterThan(afterAssign));
            Assert.That(edits, Is.EqualTo(afterEdit + 1), "a removed transition must stop reporting edits");
        });
    }

    [Test]
    public void MovingAnElement_ReportsTheTransitionsItTookPartInAsAffected()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_affected", duration: Seconds(10));
        Cut cut = AddCut(harness);
        cut.Incoming.EnterTransition = Transition(1);
        ElementEditedEventArgs? args = null;
        cut.Outgoing.Edited += (_, e) => args = e as ElementEditedEventArgs;

        cut.Outgoing.Length = Seconds(1);

        Assert.That(args?.AffectedRange, Does.Contain(TimeRange.FromRange(Seconds(2), Seconds(3))));
    }

    // The outgoing element's long enter transition stops at its middle while the incoming element's enter
    // transition runs at its end. Moving the incoming element away lets that span grow past the middle, so
    // the frames it now covers must be redrawn even though the scene no longer shows why.
    [Test]
    public void MovingANeighbourAway_ReachesTheFramesOfASpanItHeldToTheMiddle()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_released", duration: Seconds(10));
        Cut cut = AddCut(harness);
        cut.Outgoing.EnterTransition = Transition(1.5);
        cut.Incoming.EnterTransition = Transition(0.25);
        TimeRange? held = ElementTransitions.GetBoundaryAtStart(cut.Outgoing)?.Region;
        ElementEditedEventArgs? args = null;
        cut.Incoming.Edited += (_, e) => args = e as ElementEditedEventArgs;

        cut.Incoming.Start = Seconds(6);

        Assert.Multiple(() =>
        {
            Assert.That(held, Is.EqualTo(TimeRange.FromRange(TimeSpan.Zero, Seconds(1))));
            Assert.That(ElementTransitions.GetBoundaryAtStart(cut.Outgoing)?.Region, Is.EqualTo(TimeRange.FromRange(TimeSpan.Zero, Seconds(1.5))));
            Assert.That(args?.AffectedRange, Has.Some.Matches<TimeRange>(r => r.Start <= Seconds(1) && r.End >= Seconds(1.5)));
        });
    }

    // A transition of no length at one end leaves the span at the other end whole.
    [Test]
    public void ATransitionOfNoLength_DoesNotHoldTheOtherEndToTheMiddle()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_zero_length", duration: Seconds(20));
        Element element = harness.AddElement(TimeSpan.Zero, Seconds(10));
        element.Objects.Add(new RectShape());
        element.EnterTransition = Transition(0);
        element.ExitTransition = Transition(8);

        Assert.That(ElementTransitions.GetBoundaryAtEnd(element)?.Region, Is.EqualTo(TimeRange.FromRange(Seconds(2), Seconds(10))));
    }

    [Test]
    public void TheDuration_TakesNoExpression()
    {
        ClipTransition transition = Transition(1);

        Assert.Multiple(() =>
        {
            Assert.That(transition.Duration.SupportsExpression, Is.False);
            Assert.Throws<InvalidOperationException>(
                () => transition.Duration.Expression = new ConstantExpression<TimeSpan>(Seconds(2)));
            Assert.That(transition.Easing.SupportsExpression, Is.True);
        });
    }

    [Test]
    public void ChangesToEitherElement_ReachTheFramesOfTheirTransition()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_near", duration: Seconds(10));
        Cut cut = AddCut(harness);
        cut.Outgoing.ExitTransition = Transition(0.5);
        cut.Incoming.EnterTransition = Transition(0.5);
        var region = TimeRange.FromRange(Seconds(1.5), Seconds(2.5));

        Assert.Multiple(() =>
        {
            Assert.That(ElementTransitions.GetRegionsNear(harness.Scene, cut.Outgoing.Range), Does.Contain(region));
            Assert.That(ElementTransitions.GetRegionsNear(harness.Scene, cut.Incoming.Range), Does.Contain(region));
            Assert.That(ElementTransitions.GetRegionsNear(harness.Scene, TimeRange.FromRange(Seconds(5), Seconds(6))), Is.Empty);
        });
    }

    [Test]
    public void BothSides_RoundTripThroughTheElementFile()
    {
        var element = new Element { Start = Seconds(1), Length = Seconds(3) };
        element.EnterTransition = new WipeTransition
        {
            Duration = { CurrentValue = Seconds(0.75) },
            Direction = { CurrentValue = ClipTransitionDirection.BottomToTop },
            Easing = { CurrentValue = new SplineEasing(0.25f, 0.1f, 0.4f, 1f) },
        };
        element.ExitTransition = new DipToColorTransition
        {
            Duration = { CurrentValue = Seconds(0.3) },
            Color = { CurrentValue = Colors.Red },
        };

        JsonObject json = CoreSerializer.SerializeToJsonObject(element);
        var restored = (Element)CoreSerializer.DeserializeFromJsonObject(json, typeof(Element));

        Assert.Multiple(() =>
        {
            Assert.That(restored.EnterTransition, Is.TypeOf<WipeTransition>());
            Assert.That(restored.EnterTransition?.Duration.CurrentValue, Is.EqualTo(Seconds(0.75)));
            Assert.That((restored.EnterTransition as WipeTransition)?.Direction.CurrentValue, Is.EqualTo(ClipTransitionDirection.BottomToTop));
            Assert.That(restored.EnterTransition?.Easing.CurrentValue, Is.TypeOf<SplineEasing>());
            Assert.That(
                restored.EnterTransition?.Easing.CurrentValue is SplineEasing { X1: 0.25f, Y1: 0.1f, X2: 0.4f, Y2: 1f },
                Is.True,
                "the spline's control points survive the round trip");
            Assert.That(restored.ExitTransition, Is.TypeOf<DipToColorTransition>());
            Assert.That(restored.ExitTransition?.Duration.CurrentValue, Is.EqualTo(Seconds(0.3)));
            Assert.That((restored.ExitTransition as DipToColorTransition)?.Color.CurrentValue, Is.EqualTo(Colors.Red));
            Assert.That(restored.EnterTransition?.HierarchicalParent, Is.SameAs(restored));
        });
    }

    // A transition whose type is not available, such as one from an extension that is not installed, still
    // loads, blends its boundary, and is saved back as what it was.
    [Test]
    public void ATransitionOfAnUnknownType_LoadsAsAFallbackAndSavesBackAsItWas()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_fallback", duration: Seconds(10));
        Cut cut = AddCut(harness);
        cut.Incoming.EnterTransition = new WipeTransition { Duration = { CurrentValue = Seconds(0.75) } };
        string saved = CoreSerializer.SerializeToJsonObject(cut.Incoming).ToJsonString();
        var json = (JsonObject)JsonNode.Parse(saved.Replace(nameof(WipeTransition), "VanishedTransition"))!;

        var restored = (Element)CoreSerializer.DeserializeFromJsonObject(json, typeof(Element));
        harness.Scene.Children.Remove(cut.Incoming);
        harness.Scene.Children.Add(restored);
        using var compositor = new SceneCompositor(harness.Scene);
        ClipTransitionPresenter.Resource presenter = SinglePresenter(compositor.EvaluateGraphics(Seconds(2.25)));

        Assert.Multiple(() =>
        {
            Assert.That(restored.EnterTransition, Is.TypeOf<FallbackClipTransition>());
            Assert.That(restored.EnterTransition?.Duration.CurrentValue, Is.EqualTo(Seconds(0.75)));
            Assert.That(presenter.TransitionResource?.GetOriginal(), Is.SameAs(restored.EnterTransition));
            Assert.That(CoreSerializer.SerializeToJsonObject(restored).ToJsonString(), Does.Contain("VanishedTransition"));
        });
    }

    [Test]
    public void AnElementWithoutTransitions_WritesNoTransitionKeys()
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(new Element { Length = Seconds(1) });

        Assert.Multiple(() =>
        {
            Assert.That(json.ContainsKey(nameof(Element.EnterTransition)), Is.False);
            Assert.That(json.ContainsKey(nameof(Element.ExitTransition)), Is.False);
        });
    }

    [Test]
    public void SettingATransition_IsUndoable()
    {
        using var harness = new SceneHistoryHarness("beutl_transition_undo", duration: Seconds(10));
        Cut cut = AddCut(harness);
        ClipTransition transition = Transition(0.5);

        cut.Incoming.EnterTransition = transition;
        harness.History.Commit("add");
        transition.Duration.CurrentValue = Seconds(2);
        harness.History.Commit("edit");

        harness.History.Undo();
        TimeSpan durationAfterFirstUndo = transition.Duration.CurrentValue;
        harness.History.Undo();
        ClipTransition? afterSecondUndo = cut.Incoming.EnterTransition;
        harness.History.Redo();

        Assert.Multiple(() =>
        {
            Assert.That(durationAfterFirstUndo, Is.EqualTo(Seconds(0.5)));
            Assert.That(afterSecondUndo, Is.Null);
            Assert.That(cut.Incoming.EnterTransition, Is.SameAs(transition));
        });
    }

    private sealed record Cut(Element Outgoing, RectShape OutgoingShape, Element Incoming, EllipseShape IncomingShape);

    // Two elements on layer 0 meeting at two seconds.
    private static Cut AddCut(SceneHistoryHarness harness)
    {
        var outgoingShape = new RectShape();
        Element outgoing = harness.AddElement(TimeSpan.Zero, Seconds(2));
        outgoing.Objects.Add(outgoingShape);

        var incomingShape = new EllipseShape();
        Element incoming = harness.AddElement(Seconds(2), Seconds(2));
        incoming.Objects.Add(incomingShape);
        return new Cut(outgoing, outgoingShape, incoming, incomingShape);
    }

    private static ClipTransition Transition(double seconds)
        => new CrossDissolveTransition { Duration = { CurrentValue = Seconds(seconds) } };

    private static SourceVideo CreateVideo()
    {
        var source = new VideoSource();
        source.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), 300)));
        return new SourceVideo { Source = { CurrentValue = source } };
    }

    private static ClipTransitionPresenter.Resource SinglePresenter(CompositionFrame frame)
    {
        Assert.That(frame.Objects, Has.Length.EqualTo(1));
        return (ClipTransitionPresenter.Resource)frame.Objects[0];
    }

    private static object?[] Originals(CompositionFrame frame) => [.. frame.Objects.Select(r => r.GetOriginal())];

    private static TimeSpan Seconds(double seconds) => TimeSpan.FromSeconds(seconds);

    private sealed class ConstantExpression<T>(T value) : IExpression<T>
    {
        public string ExpressionString => "test constant";

        public bool Validate(out string? error)
        {
            error = null;
            return true;
        }

        public T Evaluate(ExpressionContext context) => value;
    }
}
