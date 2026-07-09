using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class AnimatablePropertyTests
{
    // A non-EngineObject root: an EngineObject root would hijack the owner's time-anchor subscription.
    private sealed class TestHierarchicalRoot : Hierarchical, IHierarchicalRoot
    {
        public event EventHandler<IHierarchical>? DescendantAttached;

        public event EventHandler<IHierarchical>? DescendantDetached;

        public void OnDescendantAttached(IHierarchical descendant)
            => DescendantAttached?.Invoke(this, descendant);

        public void OnDescendantDetached(IHierarchical descendant)
            => DescendantDetached?.Invoke(this, descendant);
    }

    private static AnimatableProperty<T> Make<T>(T defaultValue, string name = "Value")
    {
        var property = new AnimatableProperty<T>(defaultValue);
        property.SetAttributes(name, []);
        return property;
    }

    private static KeyFrameAnimation<int> CreateLinearAnimation(int from, int to, double seconds)
    {
        var animation = new KeyFrameAnimation<int>();
        animation.KeyFrames.Add(new KeyFrame<int>
        {
            KeyTime = TimeSpan.Zero,
            Value = from,
            Easing = new LinearEasing()
        });
        animation.KeyFrames.Add(new KeyFrame<int>
        {
            KeyTime = TimeSpan.FromSeconds(seconds),
            Value = to,
            Easing = new LinearEasing()
        });
        return animation;
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
    public void CurrentValue_NewValue_RaisesValueChangedAndEdited()
    {
        var property = Make(0);
        PropertyValueChangedEventArgs<int>? args = null;
        int edited = 0;
        property.ValueChanged += (_, e) => args = e;
        property.Edited += (_, _) => edited++;

        property.CurrentValue = 100;

        Assert.That(args, Is.Not.Null);
        Assert.That(args!.NewValue, Is.EqualTo(100));
        Assert.That(args.OldValue, Is.EqualTo(0));
        Assert.That(edited, Is.EqualTo(1));
        Assert.That(property.HasLocalValue, Is.True);
    }

    [Test]
    public void CurrentValue_SameValue_DoesNotRaiseEvents()
    {
        var property = Make(10);
        int edited = 0;
        property.Edited += (_, _) => edited++;

        property.CurrentValue = 10;

        Assert.That(edited, Is.Zero);
    }

    [Test]
    public void Animation_AssigningRaisesEdited()
    {
        var property = Make(0);
        IAnimation<int>? captured = null;
        int edited = 0;
        property.AnimationChanged += a => captured = a;
        property.Edited += (_, _) => edited++;

        var animation = CreateLinearAnimation(0, 10, 1);
        property.Animation = animation;

        Assert.That(captured, Is.SameAs(animation));
        Assert.That(edited, Is.EqualTo(1));
    }

    [Test]
    public void Animation_AssigningSameValue_NoEvent()
    {
        var property = Make(0);
        var animation = CreateLinearAnimation(0, 10, 1);
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
        property.Animation = CreateLinearAnimation(0, 10, 1);
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
        property.Animation = CreateLinearAnimation(0, 100, 1.0);

        var midpoint = property.GetValue(new CompositionContext(TimeSpan.FromSeconds(0.5)));

        Assert.That(midpoint, Is.InRange(40, 60));
    }

    [Test]
    public void GetValue_WithRelativeAnimation_UsesLogicalParentTimeRange()
    {
        var owner = new TestEngineObjectForProperty();
        var property = Make(0);
        property.SetOwnerObject(owner);
        property.Animation = CreateLinearAnimation(100, 0, 1.0);
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
    public void GetValue_WithoutAnimationOrExpression_ReturnsCurrentValue()
    {
        var property = Make(0);
        property.CurrentValue = 7;

        var value = property.GetValue(CompositionContext.Default);

        Assert.That(value, Is.EqualTo(7));
    }

    [Test]
    public void Expression_AssigningRaisesExpressionChangedAndEdited()
    {
        var property = Make(0.0);
        IExpression<double>? captured = null;
        int edited = 0;
        property.ExpressionChanged += e => captured = e;
        property.Edited += (_, _) => edited++;

        var expression = Expression.Create<double>("1+2");
        property.Expression = expression;

        Assert.That(captured, Is.SameAs(expression));
        Assert.That(edited, Is.EqualTo(1));
        Assert.That(property.HasExpression, Is.True);
    }

    [Test]
    public void ResetToDefault_ClearsValueAnimationAndExpression()
    {
        var property = Make(7);
        property.CurrentValue = 99;
        property.Animation = CreateLinearAnimation(0, 10, 1);
        property.Expression = Expression.Create<int>("1+2");

        property.ResetToDefault();

        Assert.That(property.CurrentValue, Is.EqualTo(7));
        Assert.That(property.HasLocalValue, Is.False);
        Assert.That(property.Animation, Is.Null);
        Assert.That(property.HasExpression, Is.False);
    }

    [Test]
    public void CompoundAssign_SetsCurrentValue()
    {
        var property = Make(0);

        property <<= 99;

        Assert.That(property.CurrentValue, Is.EqualTo(99));
    }

    [Test]
    public void Name_BeforeInitialization_Throws()
    {
        var property = new AnimatableProperty<int>(0);

        Assert.Throws<InvalidOperationException>(() => _ = property.Name);
    }

    [Test]
    public void GetOwnerObject_AfterSet_ReturnsAssignedOwner()
    {
        var property = Make(0);
        var owner = new TestEngineObjectForProperty();

        property.SetOwnerObject(owner);

        Assert.That(property.GetOwnerObject(), Is.SameAs(owner));
    }
}
