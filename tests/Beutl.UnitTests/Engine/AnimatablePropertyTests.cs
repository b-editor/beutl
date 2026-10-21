using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class AnimatablePropertyTests
{
    private static AnimatableProperty<T> Make<T>(T defaultValue, string name = "Value")
    {
        var property = new AnimatableProperty<T>(defaultValue);
        property.SetAttributes(name, []);
        return property;
    }

    [Test]
    public void Defaults_AreReportedCorrectly()
    {
        var property = Make(7);

        Assert.That(property.IsAnimatable, Is.True);
        Assert.That(property.DefaultValue, Is.EqualTo(7));
        Assert.That(property.Value, Is.EqualTo(7));
        Assert.That(property.IsValueSet, Is.False);
        Assert.That(property.HasExpression, Is.False);
        Assert.That(property.GetKeyFrameAnimation(), Is.Null);
    }

    [Test]
    public void Animation_AssigningRaisesEdited()
    {
        var property = Make(0);
        IExpression<int>? captured = null;
        int edited = 0;
        property.ExpressionChanged += e => captured = e;
        property.Edited += (_, _) => edited++;

        var animation = TestAnimations.Linear(0, 10, TimeSpan.FromSeconds(1));
        property.SetKeyFrameAnimation(animation);

        Assert.That(captured, Is.SameAs(animation));
        Assert.That(edited, Is.EqualTo(1));
    }

    [Test]
    public void Animation_AssigningSameValue_NoEvent()
    {
        var property = Make(0);
        var animation = TestAnimations.Linear(0, 10, TimeSpan.FromSeconds(1));
        property.SetKeyFrameAnimation(animation);
        int edited = 0;
        property.Edited += (_, _) => edited++;

        property.SetKeyFrameAnimation(animation);

        Assert.That(edited, Is.Zero);
    }

    [Test]
    public void Animation_ClearingToNull_RaisesEdited()
    {
        var property = Make(0);
        property.SetKeyFrameAnimation(TestAnimations.Linear(0, 10, TimeSpan.FromSeconds(1)));
        int edited = 0;
        property.Edited += (_, _) => edited++;

        property.SetKeyFrameAnimation(null);

        Assert.That(edited, Is.EqualTo(1));
        Assert.That(property.GetKeyFrameAnimation(), Is.Null);
    }

    [Test]
    public void GetValue_WithAnimation_ReturnsInterpolatedValue()
    {
        var property = Make(0);
        property.SetKeyFrameAnimation(TestAnimations.Linear(0, 100, TimeSpan.FromSeconds(1.0)));

        var midpoint = property.Evaluate(new CompositionContext(TimeSpan.FromSeconds(0.5)));

        Assert.That(midpoint, Is.InRange(40, 60));
    }

    [Test]
    public void GetValue_WithRelativeAnimation_UsesLogicalParentTimeRange()
    {
        var owner = new TestEngineObjectForProperty();
        var property = Make(0);
        property.SetOwnerObject(owner);
        property.SetKeyFrameAnimation(TestAnimations.Linear(100, 0, TimeSpan.FromSeconds(1.0)));
        var element = new Element
        {
            Start = TimeSpan.FromSeconds(5),
            Length = TimeSpan.FromSeconds(2)
        };
        element.AddObject(owner);
        // Attaching to a root caches the parent reference the local clock anchors to.
        var root = new TestHierarchicalRoot();
        ((IModifiableHierarchical)root).AddChild(element);

        int midpoint = property.Evaluate(new CompositionContext(TimeSpan.FromSeconds(5.5)));

        Assert.That(midpoint, Is.EqualTo(50));
    }

    [Test]
    public void ResetToDefault_ClearsTheAnimation()
    {
        var property = Make(7);
        property.SetKeyFrameAnimation(TestAnimations.Linear(0, 10, TimeSpan.FromSeconds(1)));

        property.ResetToDefault();

        Assert.That(property.GetKeyFrameAnimation(), Is.Null);
    }

    [Test]
    public void CompoundAssign_SetsCurrentValue()
    {
        var property = Make(0);

        property <<= 99;

        Assert.That(property.Value, Is.EqualTo(99));
    }

}
