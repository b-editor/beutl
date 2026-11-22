using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
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

    // Keyframes without a key give no value of their own, so the stored value stands, as it does for a split animation.
    [Test]
    public void Value_OverKeyFramesWithoutAKey_UsesStoredValue()
    {
        var property = Make(1.0);
        property.Value = 7;
        property.SetKeyFrameAnimation(new KeyFrameAnimation<double>());
        property.Expression = Expression.Create<double>("Value * 2");

        double value = property.Evaluate(new CompositionContext(MediaTime.FromSeconds(1)));

        Assert.That(value, Is.EqualTo(14));
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

    [TestCase(false)]
    [TestCase(true)]
    public void ComponentExpression_OnCombinedProperties_EvaluatesValueAndValueAtTime(bool hasKeyFrames)
    {
        var shape = new RectShape { Width = { Value = 100 }, Height = { Value = 200 } };
        if (hasKeyFrames)
        {
            shape.Width.SetKeyFrameAnimation(TestAnimations.Linear(0f, 100f, MediaTime.FromSeconds(1)));
            shape.Height.SetKeyFrameAnimation(TestAnimations.Linear(200f, 400f, MediaTime.FromSeconds(1)));
        }

        Assert.That(ComponentExpression<Size, float>.TryParse(
            "new Size(Value.Width + 10, ValueAtTime(Time - 0.5).Height * 2)",
            out var expressions, out var error), Is.True, error);
        shape.Width.Expression = expressions![0];
        shape.Height.Expression = expressions[1];

        var context = new CompositionContext(MediaTime.FromSeconds(0.5));
        float width = shape.Width.Evaluate(context);
        float height = shape.Height.Evaluate(context);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(width, Is.EqualTo(hasKeyFrames ? 60f : 110f).Within(1e-4f));
            Assert.That(height, Is.EqualTo(400f).Within(1e-4f));
            Assert.That(shape.Width.ExpressionError, Is.Null);
            Assert.That(shape.Height.ExpressionError, Is.Null);
        }
    }

    [Test]
    public void ComponentExpression_OnCombinedPoint_CanMixSiblingValues()
    {
        var transform = new TranslateTransform { X = { Value = 10 }, Y = { Value = 20 } };
        Assert.That(ComponentExpression<Point, float>.TryParse(
            "new Point(Value.X + Value.Y, Value.X * 3)",
            out var expressions, out var error), Is.True, error);
        transform.X.Expression = expressions![0];
        transform.Y.Expression = expressions[1];

        var context = new CompositionContext(MediaTime.Zero);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(transform.X.Evaluate(context), Is.EqualTo(30f).Within(1e-4f));
            Assert.That(transform.Y.Evaluate(context), Is.EqualTo(30f).Within(1e-4f));
            Assert.That(transform.X.ExpressionError, Is.Null);
            Assert.That(transform.Y.ExpressionError, Is.Null);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CombinedValueExpression_EvaluatesCombinedValueAndRoundTripsThroughJson(bool hasKeyFrames)
    {
        var transform = new TranslateTransform { X = { Value = 10 }, Y = { Value = 20 } };
        if (hasKeyFrames)
        {
            transform.X.SetKeyFrameAnimation(TestAnimations.Linear(0f, 100f, MediaTime.FromSeconds(1)));
            transform.Y.SetKeyFrameAnimation(TestAnimations.Linear(20f, 60f, MediaTime.FromSeconds(1)));
        }

        Assert.That(CombinedValueExpression<Point, float>.TryParse(
            "(new Point(Value.X * 2 + ValueAtTime(Time - 0.5).Y, Value.Y)).X",
            out var expression, out var error), Is.True, error);
        Assert.That(CombinedValueExpression<Point, float>.TryParse("new Point(Value.X, Value.Y)", out _, out var typeError), Is.False);
        Assert.That(typeError, Does.Contain("float"));

        IExpression<float>? copy = Expression.CreateFromNode<float>(Expression.ToNode(expression!));
        Assert.That(copy, Is.TypeOf<CombinedValueExpression<Point, float>>());
        transform.X.Expression = copy;

        float result = transform.X.Evaluate(new CompositionContext(MediaTime.FromSeconds(0.5)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo(hasKeyFrames ? 120f : 40f).Within(1e-4f));
            Assert.That(transform.X.ExpressionError, Is.Null);
            Assert.That(transform.X.GetKeyFrameAnimation() != null, Is.EqualTo(hasKeyFrames));
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
