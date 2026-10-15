using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
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
        Assert.That(property.SupportsExpression, Is.True);
        Assert.That(property.DefaultValue, Is.EqualTo(7));
        Assert.That(property.CurrentValue, Is.EqualTo(7));
        Assert.That(property.HasLocalValue, Is.False);
        Assert.That(property.HasExpression, Is.False);
        Assert.That(property.Animation, Is.Null);
    }

    [Test]
    public void Animation_AssigningRaisesEdited()
    {
        var property = Make(0);
        IAnimation<int>? captured = null;
        int edited = 0;
        property.AnimationChanged += a => captured = a;
        property.Edited += (_, _) => edited++;

        var animation = TestAnimations.Linear(0, 10, TimeSpan.FromSeconds(1));
        property.Animation = animation;

        Assert.That(captured, Is.SameAs(animation));
        Assert.That(edited, Is.EqualTo(1));
    }

    [Test]
    public void Animation_AssigningSameValue_NoEvent()
    {
        var property = Make(0);
        var animation = TestAnimations.Linear(0, 10, TimeSpan.FromSeconds(1));
        property.Animation = animation;
        int edited = 0;
        property.Edited += (_, _) => edited++;

        property.Animation = animation;

        Assert.That(edited, Is.Zero);
    }

    [Test]
    public void Animation_ClearingToNull_RaisesEdited()
    {
        var property = Make(0);
        property.Animation = TestAnimations.Linear(0, 10, TimeSpan.FromSeconds(1));
        int edited = 0;
        property.Edited += (_, _) => edited++;

        property.Animation = null;

        Assert.That(edited, Is.EqualTo(1));
        Assert.That(property.Animation, Is.Null);
    }

    [Test]
    public void GetValue_WithAnimation_ReturnsInterpolatedValue()
    {
        var property = Make(0);
        property.Animation = TestAnimations.Linear(0, 100, TimeSpan.FromSeconds(1.0));

        var midpoint = property.GetValue(new CompositionContext(TimeSpan.FromSeconds(0.5)));

        Assert.That(midpoint, Is.InRange(40, 60));
    }

    [Test]
    public void GetValue_WithRelativeAnimation_UsesLogicalParentTimeRange()
    {
        var owner = new TestEngineObjectForProperty();
        var property = Make(0);
        property.SetOwnerObject(owner);
        property.Animation = TestAnimations.Linear(100, 0, TimeSpan.FromSeconds(1.0));
        var element = new Element
        {
            Start = TimeSpan.FromSeconds(5),
            Length = TimeSpan.FromSeconds(2)
        };
        element.AddObject(owner);
        // Attaching to a root caches the parent reference the local clock anchors to.
        var root = new TestHierarchicalRoot();
        ((IModifiableHierarchical)root).AddChild(element);

        int midpoint = property.GetValue(new CompositionContext(TimeSpan.FromSeconds(5.5)));

        Assert.That(midpoint, Is.EqualTo(50));
    }

    [Test]
    public void ResetToDefault_ClearsTheAnimation()
    {
        var property = Make(7);
        property.Animation = TestAnimations.Linear(0, 10, TimeSpan.FromSeconds(1));

        property.ResetToDefault();

        Assert.That(property.Animation, Is.Null);
    }
}
