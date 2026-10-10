using Beutl.Animation.Easings;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Engine.Expressions;
using Beutl.Graphics.Transitions;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class ElementAttributeServiceTests
{
    private SceneHistoryHarness _harness = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private ElementAttributeService _service = null!;

    [SetUp]
    public void Setup()
    {
        _harness = new SceneHistoryHarness("beutl_attr", start: TimeSpan.Zero, duration: TimeSpan.FromSeconds(60));
        _scene = _harness.Scene;
        _history = _harness.History;
        _service = new ElementAttributeService(_history);
    }

    [TearDown]
    public void TearDown()
    {
        _harness.Dispose();
    }

    private Element AddElement(bool isEnabled = true)
        => _harness.AddElement(isEnabled);

    [Test]
    public void Constructor_NullHistoryManager_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ElementAttributeService(null!));
    }

    [Test]
    public void SetEnabled_TogglesAndCommitsOnce()
    {
        Element element = AddElement();
        int before = _history.UndoCount;

        _service.SetEnabled(element, false);

        Assert.Multiple(() =>
        {
            Assert.That(element.IsEnabled, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void SetEnabled_NoChange_NoCommit()
    {
        Element element = AddElement();
        int before = _history.UndoCount;

        _service.SetEnabled(element, element.IsEnabled);

        Assert.That(_history.UndoCount, Is.EqualTo(before));
    }

    [Test]
    public void SetAccentColor_AppliesAndCommitsOnce()
    {
        Element element = AddElement();
        int before = _history.UndoCount;
        var target = Color.FromArgb(255, 10, 20, 30);

        _service.SetAccentColor(element, target);

        Assert.Multiple(() =>
        {
            Assert.That(element.AccentColor, Is.EqualTo(target));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void SetAccentColor_LockedElement_IsIgnored()
    {
        Element element = AddElement();
        Color initial = element.AccentColor;
        element.IsLocked = true;
        int before = _history.UndoCount;

        _service.SetAccentColor(element, Color.FromArgb(255, 10, 20, 30));

        Assert.Multiple(() =>
        {
            Assert.That(element.AccentColor, Is.EqualTo(initial), "a locked clip's color must not change");
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void SetAccentColor_NoChange_NoCommit()
    {
        Element element = AddElement();
        Color initial = element.AccentColor;
        int before = _history.UndoCount;

        _service.SetAccentColor(element, initial);

        Assert.That(_history.UndoCount, Is.EqualTo(before));
    }

    [Test]
    public void SetLocked_TogglesAndCommitsOnce()
    {
        Element element = AddElement();
        int before = _history.UndoCount;

        _service.SetLocked(element, true);

        Assert.Multiple(() =>
        {
            Assert.That(element.IsLocked, Is.True);
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void SetLocked_NoChange_NoCommit()
    {
        Element element = AddElement();
        int before = _history.UndoCount;

        _service.SetLocked(element, element.IsLocked);

        Assert.That(_history.UndoCount, Is.EqualTo(before));
    }

    [Test]
    public void SetLocked_NullElement_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _service.SetLocked(null!, true));
    }

    [Test]
    public void ApplyTransition_AtACut_CentresTheDefaultOnItWithOneCommit()
    {
        (Element outgoing, Element incoming) = AddCut();
        int before = _history.UndoCount;

        _service.ApplyTransition(incoming, ElementEdge.Start, typeof(CrossDissolveTransition));

        Assert.Multiple(() =>
        {
            Assert.That(incoming.EnterTransition?.Duration.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(0.25)));
            Assert.That(outgoing.ExitTransition?.Duration.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(0.25)));
            Assert.That(incoming.EnterTransition, Is.TypeOf<CrossDissolveTransition>());
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });

        _history.Undo();
        Assert.Multiple(() =>
        {
            Assert.That(incoming.EnterTransition, Is.Null);
            Assert.That(outgoing.ExitTransition, Is.Null);
        });
    }

    [Test]
    public void ApplyTransition_AtALoneEdge_GivesItTheWholeDefault()
    {
        Element element = AddElement();

        _service.ApplyTransition(element, ElementEdge.End, typeof(FadeTransition));

        Assert.Multiple(() =>
        {
            Assert.That(element.ExitTransition?.Duration.CurrentValue, Is.EqualTo(ClipTransition.DefaultDuration));
            Assert.That(element.ExitTransition, Is.TypeOf<FadeTransition>());
        });
    }

    [Test]
    public void ApplyTransition_OnAnExistingBoundary_ChangesBothSidesTypeAndKeepsTheirTiming()
    {
        (Element outgoing, Element incoming) = AddCut();
        _service.ApplyTransition(incoming, ElementEdge.Start, typeof(CrossDissolveTransition));
        ClipTransition enter = incoming.EnterTransition!;
        var easing = new CubicEaseInOut();
        enter.Duration.CurrentValue = TimeSpan.FromSeconds(1);
        enter.Easing.CurrentValue = easing;
        _history.Commit("duration");
        int before = _history.UndoCount;

        _service.ApplyTransition(outgoing, ElementEdge.End, typeof(WipeTransition));

        Assert.Multiple(() =>
        {
            Assert.That(incoming.EnterTransition, Is.TypeOf<WipeTransition>());
            Assert.That(outgoing.ExitTransition, Is.TypeOf<WipeTransition>());
            Assert.That(incoming.EnterTransition?.Duration.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(outgoing.ExitTransition?.Duration.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(0.25)));
            Assert.That(incoming.EnterTransition?.Easing.CurrentValue, Is.SameAs(easing));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });

        _history.Undo();
        Assert.That(incoming.EnterTransition, Is.SameAs(enter), "undo brings back the transition it replaced");
    }

    [Test]
    public void ApplyTransition_ChangingTheType_KeepsAnEasingExpression()
    {
        (_, Element incoming) = AddCut();
        _service.ApplyTransition(incoming, ElementEdge.Start, typeof(CrossDissolveTransition));
        var expression = new ConstantEasingExpression(new CubicEaseInOut());
        incoming.EnterTransition!.Easing.Expression = expression;

        _service.ApplyTransition(incoming, ElementEdge.Start, typeof(WipeTransition));

        Assert.That(incoming.EnterTransition?.Easing.Expression, Is.SameAs(expression));
    }

    // The element after a cut decides how the boundary blends, so when it is locked with an enter
    // transition, picking a type at the end before it would commit a change nobody can see.
    [Test]
    public void ApplyTransition_AtAnEndBeforeALockedDecidingSide_ChangesNothing()
    {
        (Element outgoing, Element incoming) = AddCut();
        incoming.EnterTransition = new WipeTransition();
        incoming.IsLocked = true;
        _history.Commit("lock");
        int before = _history.UndoCount;

        _service.ApplyTransition(outgoing, ElementEdge.End, typeof(FadeTransition));

        Assert.Multiple(() =>
        {
            Assert.That(outgoing.ExitTransition, Is.Null);
            Assert.That(incoming.EnterTransition, Is.TypeOf<WipeTransition>());
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void ApplyTransition_OfTheSameType_ChangesNothing()
    {
        (_, Element incoming) = AddCut();
        _service.ApplyTransition(incoming, ElementEdge.Start, typeof(CrossDissolveTransition));
        ClipTransition enter = incoming.EnterTransition!;
        int before = _history.UndoCount;

        _service.ApplyTransition(incoming, ElementEdge.Start, typeof(CrossDissolveTransition));

        Assert.Multiple(() =>
        {
            Assert.That(incoming.EnterTransition, Is.SameAs(enter));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [TestCase(typeof(ClipTransition))]
    [TestCase(typeof(FallbackClipTransition))]
    [TestCase(typeof(Element))]
    public void ApplyTransition_RejectsATypeThatIsNotACreatableTransition(Type type)
    {
        Element element = AddElement();

        Assert.Throws<ArgumentException>(() => _service.ApplyTransition(element, ElementEdge.Start, type));
    }

    [Test]
    public void RemoveTransition_ClearsBothSidesOfTheBoundary()
    {
        (Element outgoing, Element incoming) = AddCut();
        _service.ApplyTransition(incoming, ElementEdge.Start, typeof(CrossDissolveTransition));
        int before = _history.UndoCount;

        _service.RemoveTransition(incoming, ElementEdge.Start);

        Assert.Multiple(() =>
        {
            Assert.That(incoming.EnterTransition, Is.Null);
            Assert.That(outgoing.ExitTransition, Is.Null);
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    // Removing only the unlocked side would leave the locked element's side blending the boundary.
    [Test]
    public void RemoveTransition_LeavesABoundaryALockedSideHolds()
    {
        (Element outgoing, Element incoming) = AddCut();
        _service.ApplyTransition(incoming, ElementEdge.Start, typeof(CrossDissolveTransition));
        incoming.IsLocked = true;
        _history.Commit("lock");
        int before = _history.UndoCount;

        _service.RemoveTransition(outgoing, ElementEdge.End);

        Assert.Multiple(() =>
        {
            Assert.That(ElementTransitionEdits.HasLockedSideAcross(outgoing, ElementEdge.End), Is.True);
            Assert.That(outgoing.ExitTransition, Is.Not.Null);
            Assert.That(incoming.EnterTransition, Is.Not.Null);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void ApplyTransition_LeavesALockedPartnerAlone()
    {
        (Element outgoing, Element incoming) = AddCut();
        outgoing.IsLocked = true;
        _history.Commit("lock");

        _service.ApplyTransition(incoming, ElementEdge.Start, typeof(CrossDissolveTransition));

        Assert.Multiple(() =>
        {
            Assert.That(outgoing.ExitTransition, Is.Null);
            Assert.That(incoming.EnterTransition?.Duration.CurrentValue, Is.EqualTo(ClipTransition.DefaultDuration));
        });
    }

    [Test]
    public void ApplyTransition_LockedElement_IsIgnored()
    {
        Element element = AddElement();
        element.IsLocked = true;
        _history.Commit("lock");
        int before = _history.UndoCount;

        _service.ApplyTransition(element, ElementEdge.Start, typeof(CrossDissolveTransition));

        Assert.Multiple(() =>
        {
            Assert.That(element.EnterTransition, Is.Null);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void SetTransitionDuration_ChangesOnlyThisSide()
    {
        (Element outgoing, Element incoming) = AddCut();
        _service.ApplyTransition(incoming, ElementEdge.Start, typeof(CrossDissolveTransition));
        int before = _history.UndoCount;

        _service.SetTransitionDuration(incoming, ElementEdge.Start, TimeSpan.FromSeconds(1));
        _service.SetTransitionDuration(incoming, ElementEdge.Start, TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(incoming.EnterTransition?.Duration.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(outgoing.ExitTransition?.Duration.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(0.25)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1), "an unchanged duration must not commit");
        });
    }

    private (Element Outgoing, Element Incoming) AddCut()
    {
        Element outgoing = _harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Element incoming = _harness.AddElement(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2));
        _history.Commit("cut");
        return (outgoing, incoming);
    }

    private sealed class ConstantEasingExpression(Easing value) : IExpression<Easing>
    {
        public string ExpressionString => "test constant";

        public bool Validate(out string? error)
        {
            error = null;
            return true;
        }

        public Easing Evaluate(ExpressionContext context) => value;
    }
}
