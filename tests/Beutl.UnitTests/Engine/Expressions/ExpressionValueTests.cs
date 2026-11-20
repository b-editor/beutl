using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Engine.Expressions;

internal sealed partial class ExpressionValueTestObject : EngineObject
{
    public ExpressionValueTestObject()
    {
        ScanProperties<ExpressionValueTestObject>();
    }

    public IExpressionProperty<double> Value { get; } = Property.CreateAnimatable(0.0);
}

[TestFixture]
public class ExpressionValueTests
{
    [Test]
    public void Value_OffsetsKeyFrames()
    {
        var property = Make(0.0);
        property.SetKeyFrameAnimation(TestAnimations.Linear(0.0, 100.0, MediaTime.FromSeconds(1)));
        property.Expression = Expression.Create<double>("Value + 5");

        double value = property.Evaluate(new CompositionContext(MediaTime.FromSeconds(0.25)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.EqualTo(30).Within(1e-4));
            Assert.That(property.ExpressionError, Is.Null);
        }
    }

    [TestCase("Value * 2")]
    [TestCase("ValueAtTime(Time - 0.5) * 2")]
    public void Value_WithoutKeyFrames_UsesStoredValue(string expression)
    {
        var property = Make(1.0);
        property.Value = 7;
        property.Expression = Expression.Create<double>(expression);

        double value = property.Evaluate(new CompositionContext(MediaTime.FromSeconds(1)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.EqualTo(14));
            Assert.That(property.Value, Is.EqualTo(7));
            Assert.That(property.ExpressionError, Is.Null);
        }
    }

    [Test]
    public void ValueAtTime_UsesOwnerLocalTime()
    {
        var owner = new AnimationSamplerTestRoot
        {
            TimeRange = { Value = new TimeRange(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2)) }
        };
        var property = Make(0.0);
        property.SetOwnerObject(owner);
        property.SetKeyFrameAnimation(TestAnimations.Linear(0.0, 100.0, MediaTime.FromSeconds(1)));
        property.Expression = Expression.Create<double>("ValueAtTime(Time - 0.5)");

        double value = property.Evaluate(new CompositionContext(MediaTime.FromSeconds(5.75)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.EqualTo(25).Within(1e-4));
            Assert.That(property.ExpressionError, Is.Null);
        }
    }

    [TestCase("Value", 3f)]
    [TestCase("new Point(Value.X + 1, Value.Y)", 4f)]
    public void Value_CompilesForPointMembers(string expression, float expectedX)
    {
        var property = Make(new Point(3, 4));
        property.Expression = Expression.Create<Point>(expression);

        Point value = property.Evaluate(new CompositionContext(MediaTime.Zero));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.EqualTo(new Point(expectedX, 4)));
            Assert.That(property.ExpressionError, Is.Null);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void GetPropertyAtTime_UsesCallerLocalTime(bool useGuid)
    {
        var (caller, target) = MakeObjects(5, 4);
        target.Value.SetKeyFrameAnimation(TestAnimations.Linear(0.0, 100.0, MediaTime.FromSeconds(2)));
        caller.Value.Expression = Expression.Create<double>(useGuid
            ? $"GetProperty<double>(new Guid(\"{target.Id.Value}\"), \"Value\", Time - 0.5)"
            : $"GetProperty<double>(\"{target.Id.Value}.Value\", Time - 0.5)");

        double value = caller.Value.Evaluate(new CompositionContext(MediaTime.FromSeconds(5.75)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.EqualTo(62.5).Within(1e-4));
            Assert.That(caller.Value.ExpressionError, Is.Null);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void GetProperty_ReferencedExpressionUsesItsOwnTime(bool timed)
    {
        var (caller, target) = MakeObjects(0, 2);
        target.Value.Expression = Expression.Create<double>("Time");
        string timeArgument = timed ? ", Time" : "";
        caller.Value.Expression = Expression.Create<double>($"GetProperty<double>(\"{target.Id.Value}.Value\"{timeArgument})");

        double value = caller.Value.Evaluate(new CompositionContext(MediaTime.FromSeconds(3)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.EqualTo(1));
            Assert.That(caller.Value.ExpressionError, Is.Null);
            Assert.That(target.Value.ExpressionError, Is.Null);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void GetProperty_RestoresCurrentPropertyAfterEvaluation(bool fails)
    {
        var (caller, target) = MakeObjects(0, 0);
        caller.Value.Value = 7;
        target.Value.Value = 41;
        target.Value.Expression = Expression.Create<double>(fails ? "throw new InvalidOperationException();" : "Value + 1");
        caller.Value.Expression = Expression.Create<double>($"GetProperty<double>(\"{target.Id.Value}.Value\") + Value");
        var context = new ExpressionContext(MediaTime.Zero, caller.Value, new PropertyLookup((ICoreObject)caller.FindHierarchicalRoot()!));

        double value = caller.Value.Evaluate(context);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.EqualTo(fails ? 48 : 49));
            Assert.That(context.CurrentProperty, Is.SameAs(caller.Value));
            Assert.That(context.IsEvaluating(caller.Value), Is.False);
            Assert.That(context.IsEvaluating(target.Value), Is.False);
            Assert.That(caller.Value.ExpressionError, Is.Null);
            Assert.That(target.Value.ExpressionError, fails ? Is.Not.Null : Is.Null);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void GetProperty_SimplePropertyExpressionUsesItsOwnGlobals(bool timed)
    {
        var caller = new TestEngineObject { Value = { Value = 10 } };
        var target = new TestEngineObject
        {
            TimeRange = { Value = new TimeRange(MediaTime.FromSeconds(2), MediaTime.FromSeconds(4)) },
            Value = { Value = 7 }
        };
        caller.AddChild(target);
        target.Value.Expression = Expression.Create<double>("Time + Value");
        string timeArgument = timed ? ", Time" : "";
        caller.Value.Expression = Expression.Create<double>($"GetProperty<double>(\"{target.Id.Value}.Value\"{timeArgument}) + Value");
        var context = new ExpressionContext(MediaTime.FromSeconds(3), caller.Value, new PropertyLookup(caller));

        double value = caller.Value.Evaluate(context);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.EqualTo(18));
            Assert.That(context.CurrentProperty, Is.SameAs(caller.Value));
            Assert.That(caller.Value.ExpressionError, Is.Null);
            Assert.That(target.Value.ExpressionError, Is.Null);
        }
    }

    [Test]
    public void GetPropertyAtTime_CycleTerminates()
    {
        var (caller, target) = MakeObjects(0, 0);
        caller.Value.Expression = Expression.Create<double>($"GetProperty<double>(\"{target.Id.Value}.Value\", Time - 1) + 1");
        target.Value.Expression = Expression.Create<double>($"GetProperty<double>(new Guid(\"{caller.Id.Value}\"), \"Value\", Time - 1) + 1");

        double value = caller.Value.Evaluate(new CompositionContext(MediaTime.FromSeconds(3)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(double.IsFinite(value), Is.True);
            Assert.That(value, Is.EqualTo(2));
            Assert.That(caller.Value.ExpressionError, Is.Null);
            Assert.That(target.Value.ExpressionError, Is.Null);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ComponentExpression_ValueReportsErrorAndFallsBack(bool hasKeyFrames)
    {
        var property = Make(7f);
        if (hasKeyFrames)
            property.SetKeyFrameAnimation(TestAnimations.Linear(0f, 100f, MediaTime.FromSeconds(1)));
        property.Expression = new ComponentExpression<Point, float>("Value", "X");

        float value = property.Evaluate(new CompositionContext(MediaTime.FromSeconds(0.5)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.EqualTo(hasKeyFrames ? 50f : 7f).Within(1e-4f));
            Assert.That(property.ExpressionError, Does.Contain("Value is not available in this expression"));
        }
    }

    [Test]
    public void Value_NullFromBelow_UsesStoredValue()
    {
        var property = Make<string?>("stored");
        var context = TestHelper.CreateExpressionContext(MediaTime.Zero, property);
        var globals = new ExpressionGlobals<string?>(context, Expression.Create<string?>("null"));

        Assert.That(globals.Value, Is.EqualTo("stored"));
    }

    private static AnimatableProperty<T> Make<T>(T value)
    {
        var property = new AnimatableProperty<T>(value);
        property.SetAttributes("Value", []);
        return property;
    }

    private static (ExpressionValueTestObject Caller, ExpressionValueTestObject Target) MakeObjects(double callerStart, double targetStart)
    {
        var root = new AnimationSamplerTestRoot
        {
            TimeRange = { Value = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(10)) }
        };
        var caller = new ExpressionValueTestObject
        {
            IsTimeAnchor = { Value = true },
            TimeRange = { Value = new TimeRange(MediaTime.FromSeconds(callerStart), MediaTime.FromSeconds(4)) }
        };
        var target = new ExpressionValueTestObject
        {
            IsTimeAnchor = { Value = true },
            TimeRange = { Value = new TimeRange(MediaTime.FromSeconds(targetStart), MediaTime.FromSeconds(4)) }
        };
        ((IModifiableHierarchical)root).AddChild(caller);
        ((IModifiableHierarchical)root).AddChild(target);
        return (caller, target);
    }
}
