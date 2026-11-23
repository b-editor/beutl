using System.Numerics;
using Beutl.Animation;
using Beutl.Animation.Animators;
using Beutl.Animation.Easings;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Engine.Expressions;

[TestFixture]
public class ExpressionLoopTests
{
    [TestCase("LoopOut", LoopMode.Cycle, 3.5, 6)]
    [TestCase("LoopOut", LoopMode.PingPong, 3.5, 16)]
    [TestCase("LoopOut", LoopMode.Offset, 3.5, 26)]
    [TestCase("LoopOut", LoopMode.Continue, 3.5, 32)]
    [TestCase("LoopIn", LoopMode.Cycle, 0.5, 16)]
    [TestCase("LoopIn", LoopMode.PingPong, 0.5, 6)]
    [TestCase("LoopIn", LoopMode.Offset, 0.5, -4)]
    [TestCase("LoopIn", LoopMode.Continue, 0.5, 2)]
    public void ModesExtendScalarAndVector(string function, LoopMode mode, double time, double expected)
    {
        string script = $"{function}(LoopMode.{mode})";
        var scalar = MakeProperty(0.0, script, MakeAnimation((1, 4.0), (2, 8.0), (3, 24.0)));
        var vector = MakeProperty(Vector2.Zero, script, MakeAnimation(
            (1, new Vector2(4, -8)), (2, new Vector2(8, -16)), (3, new Vector2(24, -48))));

        double scalarValue = Evaluate(scalar, Seconds(time));
        Vector2 vectorValue = Evaluate(vector, Seconds(time));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scalarValue, Is.EqualTo(expected).Within(0.001));
            Assert.That(vectorValue.X, Is.EqualTo(expected).Within(0.001));
            Assert.That(vectorValue.Y, Is.EqualTo(-2 * expected).Within(0.002));
        }
    }

    [TestCase("LoopOut", 3.5, 0, 6)]
    [TestCase("LoopOut", 3.5, 1, 24)]
    [TestCase("LoopOut", 3.5, 2, 16)]
    [TestCase("LoopOut", 3.5, 3, 6)]
    [TestCase("LoopOut", 3.5, 99, 6)]
    [TestCase("LoopIn", 0.5, 0, 16)]
    [TestCase("LoopIn", 0.5, 1, 4)]
    [TestCase("LoopIn", 0.5, 2, 6)]
    [TestCase("LoopIn", 0.5, 3, 16)]
    [TestCase("LoopIn", 0.5, 99, 16)]
    public void KeyframeCountLimitsTheRange(string function, double time, int keyframes, double expected)
    {
        var property = MakeProperty(0.0, $"{function}(keyframes: {keyframes})", MakeAnimation((1, 4.0), (2, 8.0), (3, 24.0)));

        Assert.That(Evaluate(property, Seconds(time)), Is.EqualTo(expected));
    }

    [TestCase("LoopOut")]
    [TestCase("LoopIn")]
    public void NegativeKeyframeCountThrows(string function)
    {
        var expression = Expression.Create<double>($"{function}(keyframes: -1)");

        Assert.That(() => expression.Evaluate(TestHelper.CreateExpressionContext(MediaTime.Zero)),
            Throws.TypeOf<ExpressionException>().With.Message.Contains("non-negative"));
    }

    [TestCase(-1, 11)]
    [TestCase(0, 11)]
    [TestCase(1, 17)]
    [TestCase(2, 17)]
    public void LoopsReturnValueWithoutTwoDistinctKeyframeTimes(int keyCount, double expected)
    {
        KeyFrameAnimation<double>? animation = keyCount < 0 ? null : MakeAnimation(
            Enumerable.Repeat((1.0, 17.0), keyCount).ToArray());
        var property = MakeProperty(11.0, "LoopOut()", animation);

        Assert.That(Evaluate(property, Seconds(2)), Is.EqualTo(expected));

        property.Expression = Expression.Create<double>("LoopIn()");
        Assert.That(Evaluate(property, MediaTime.Zero), Is.EqualTo(expected));
    }

    [TestCase("LoopOut()", 0, 4)]
    [TestCase("LoopOut()", 1.5, 6)]
    [TestCase("LoopOut()", 3, 24)]
    [TestCase("LoopIn()", 1, 4)]
    [TestCase("LoopIn()", 1.5, 6)]
    [TestCase("LoopIn()", 4, 24)]
    public void InactiveLoopReturnsValue(string script, double time, double expected)
    {
        var property = MakeProperty(0.0, script, MakeAnimation((1, 4.0), (2, 8.0), (3, 24.0)));

        Assert.That(Evaluate(property, Seconds(time)), Is.EqualTo(expected));
    }

    [TestCase("LoopOut()", false, 8.5, 6)]
    [TestCase("LoopOut()", true, 3.5, 6)]
    [TestCase("LoopIn()", false, 5.5, 16)]
    [TestCase("LoopIn()", true, 0.5, 16)]
    public void LoopsUseTheAnimationsClock(string script, bool globalClock, double time, double expected)
    {
        var animation = MakeAnimation((1, 4.0), (2, 8.0), (3, 24.0));
        animation.UseGlobalClock.Value = globalClock;
        var property = MakeProperty(0.0, script, animation, MakeOwner());

        Assert.That(Evaluate(property, Seconds(time)), Is.EqualTo(expected));
    }

    [TestCase("LoopOut", 11, 0, 20, 40)]
    [TestCase("LoopOut", 11, 2, 30, 60)]
    [TestCase("LoopOut", 11, 3, 20, 40)]
    [TestCase("LoopIn", 5, 0, 30, 60)]
    [TestCase("LoopIn", 5, 2, 20, 40)]
    [TestCase("LoopIn", 5, 3, 30, 60)]
    public void SeparatedAnimationUsesUnionOfDistinctSceneTimes(string function, double time, int keyframes, float x, float y)
    {
        var property = MakeProperty(Vector2.Zero, $"{function}(keyframes: {keyframes})", owner: MakeOwner());
        IComponentAnimation separated = ComponentAnimations.Separate(property);
        separated.Components[0].SetKeyFrameAnimation(MakeAnimation((1, 10f), (3, 30f)));
        var global = MakeAnimation((8, 40f), (10, 80f));
        global.UseGlobalClock.Value = true;
        separated.Components[1].SetKeyFrameAnimation(global);

        Assert.That(Evaluate(property, Seconds(time)), Is.EqualTo(new Vector2(x, y)));
    }

    [TestCase("LoopOut()", 7, 30, 40)]
    [TestCase("LoopIn()", 0, 30, 60)]
    // Each sibling continues along its own boundary segment; one without a key at the boundary holds.
    [TestCase("LoopOut(LoopMode.Continue)", 7, 30, 120)]
    [TestCase("LoopIn(LoopMode.Continue)", 0, 0, 40)]
    public void SharedAndEditedComponentExpressionsUseCombinedRange(string script, double time, float x, float y)
    {
        var transform = new TranslateTransform();
        transform.X.SetKeyFrameAnimation(MakeAnimation((1, 10f), (3, 30f)));
        transform.Y.SetKeyFrameAnimation(MakeAnimation((3, 40f), (5, 80f)));
        Assert.That(ComponentExpression<Point, float>.TryParse(script, out var expressions, out var error), Is.True, error);
        transform.X.Expression = expressions![0];
        transform.Y.Expression = expressions[1];

        Assert.That(Evaluate(transform.X, Seconds(time)), Is.EqualTo(x));
        Assert.That(Evaluate(transform.Y, Seconds(time)), Is.EqualTo(y));

        var combined = (ICombinedValueExpression<float>)expressions[1];
        Assert.That(combined.TryParseSameContext($"({script}).Y + 1", out var edited, out error), Is.True, error);
        Assert.That(edited, Is.TypeOf<CombinedValueExpression<Point, float>>());
        transform.Y.Expression = edited;
        Assert.That(Evaluate(transform.Y, Seconds(time)), Is.EqualTo(y + 1));
    }

    [TestCase(LoopMode.Offset)]
    [TestCase(LoopMode.Continue)]
    public void ArithmeticModesReportAnErrorForUnsupportedTypes(LoopMode mode)
    {
        var property = MakeProperty(false, $"LoopOut(LoopMode.{mode})", TestAnimations.Linear(false, true, Seconds(1)));
        bool value = property.Evaluate(new CompositionContext(Seconds(1.5)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.True);
            Assert.That(property.ExpressionError, Does.Contain(mode.ToString()).And.Contain("Boolean"));
        }
    }

    [TestCase(LoopMode.Offset)]
    [TestCase(LoopMode.Continue)]
    public void ArithmeticModesReportAnErrorForUnsupportedTypesInsideTheRange(LoopMode mode)
    {
        var property = MakeProperty(false, $"LoopOut(LoopMode.{mode})", TestAnimations.Linear(false, true, Seconds(1)));
        property.Evaluate(new CompositionContext(Seconds(0.5)));

        Assert.That(property.ExpressionError, Does.Contain(mode.ToString()).And.Contain("Boolean"));
    }

    [TestCase(LoopMode.Cycle, false)]
    [TestCase(LoopMode.PingPong, true)]
    public void SamplingModesWorkForUnsupportedArithmeticTypes(LoopMode mode, bool expected)
    {
        var property = MakeProperty(false, $"LoopOut(LoopMode.{mode})", TestAnimations.Linear(false, true, Seconds(1)));

        Assert.That(Evaluate(property, Seconds(3)), Is.EqualTo(expected));
    }

    [TestCase("LoopOut", LoopMode.Cycle, 5, 4)]
    [TestCase("LoopOut", LoopMode.PingPong, 5, 4)]
    [TestCase("LoopOut", LoopMode.PingPong, 7, 24)]
    [TestCase("LoopOut", LoopMode.Offset, 5, 44)]
    [TestCase("LoopIn", LoopMode.Cycle, -1, 4)]
    [TestCase("LoopIn", LoopMode.PingPong, -1, 24)]
    [TestCase("LoopIn", LoopMode.PingPong, -3, 4)]
    [TestCase("LoopIn", LoopMode.Offset, -1, -16)]
    public void LoopUsesExactCycleBoundaries(string function, LoopMode mode, double time, double expected)
    {
        var property = MakeProperty(0.0, $"{function}(LoopMode.{mode})", MakeAnimation((1, 4.0), (3, 24.0)));

        Assert.That(Evaluate(property, Seconds(time)), Is.EqualTo(expected));
    }

    [TestCase("LoopOut()", 1, 0)]
    [TestCase("LoopOut()", 1, 4)]
    [TestCase("LoopIn()", -1, 0)]
    [TestCase("LoopIn()", -1, 4)]
    public void LoopDoesNotDriftAfterAnHour(string script, int direction, long phase)
    {
        const long period = 11;
        long wholeCycles = MediaTime.FlicksPerSecond * 3600 / period + 1;
        var animation = TestAnimations.Linear(10.0, 20.0, new MediaTime(period));
        var property = MakeProperty(0.0, script, animation);
        var time = new MediaTime(direction * wholeCycles * period + phase);

        Assert.That(Evaluate(property, time), Is.EqualTo(animation.EvaluateAt(new MediaTime(phase))));
    }

    [Test]
    public void ContinueHoldsAtAFlatEaseOut([Values] bool quadratic)
    {
        var animation = TestAnimations.Linear(0.0, 100.0, Seconds(1));
        animation.KeyFrames[^1].Easing.Value = quadratic ? new QuadraticEaseOut() : new CubicEaseOut();
        var property = MakeProperty(0.0, "LoopOut(LoopMode.Continue)", animation);

        Assert.That(Evaluate(property, Seconds(2)), Is.EqualTo(100));
    }

    [TestCase("LoopOut(LoopMode.Continue)", 1.0002, 202)]
    [TestCase("LoopIn(LoopMode.Continue)", -0.0001, -1)]
    public void ContinueSamplesWithinAShortBoundarySegment(string script, double time, double expected)
    {
        var animation = MakeAnimation((0, 0.0), (0.0001, 1.0), (1, 200.0), (1.0001, 201.0));
        var property = MakeProperty(0.0, script, animation);

        Assert.That(Evaluate(property, Seconds(time)), Is.EqualTo(expected));
    }

    [TestCase("LoopOut(LoopMode.Continue)", 2.0, 200)]
    [TestCase("LoopIn(LoopMode.Continue)", -1.0, -100)]
    public void ContinueKeepsTheVelocityOfIntegerAnimations(string script, double time, int expected)
    {
        var property = MakeProperty(0, script, TestAnimations.Linear(0, 100, Seconds(1)));

        Assert.That(Evaluate(property, Seconds(time)), Is.EqualTo(expected));
    }

    [Test]
    public void ContinueKeepsTheVelocityOfIntegerComponents()
    {
        var property = MakeProperty(default(PixelPoint), "LoopOut(LoopMode.Continue)",
            TestAnimations.Linear(new PixelPoint(0, 10), new PixelPoint(100, 10), Seconds(1)));

        Assert.That(Evaluate(property, Seconds(3)), Is.EqualTo(new PixelPoint(300, 10)));
    }

    [Test]
    public void ContinueKeepsTheVelocityOfSeparatedIntegerComponents()
    {
        var property = MakeProperty(default(PixelPoint), "LoopOut(LoopMode.Continue)",
            TestAnimations.Linear(new PixelPoint(0, 10), new PixelPoint(100, 10), Seconds(1)));
        ComponentAnimations.Separate(property);

        Assert.That(Evaluate(property, Seconds(3)), Is.EqualTo(new PixelPoint(300, 10)));
    }

    [Test]
    public void ContinueHoldsTheValueAfterAJumpAtTheBoundary()
    {
        var property = MakeProperty(0.0, "LoopOut(LoopMode.Continue)", MakeAnimation((0, 0.0), (1, 10.0), (1, 50.0)));
        Color from = Color.FromArgb(0, 10, 20, 30);
        var color = MakeProperty(from, "LoopOut(LoopMode.Continue)", MakeAnimation(
            (0, from), (1, Color.FromArgb(200, 10, 20, 30)), (1, Color.FromArgb(10, 10, 20, 30))));

        Assert.That(Evaluate(property, Seconds(2)), Is.EqualTo(50));
        Assert.That(Evaluate(color, Seconds(2)).A, Is.EqualTo(10));
    }

    [Test]
    public void ContinueCarriesOnThroughEqualKeysAtTheBoundary()
    {
        var property = MakeProperty(0.0, "LoopOut(LoopMode.Continue)", MakeAnimation((0, 0.0), (1, 10.0), (1, 10.0)));

        Assert.That(Evaluate(property, Seconds(2)), Is.EqualTo(20));
    }

    [Test]
    public void OffsetSaturatesUnsignedValues()
    {
        var property = MakeProperty(0u, "LoopIn(LoopMode.Offset)", TestAnimations.Linear(10u, 30u, Seconds(1)));

        Assert.That(Evaluate(property, Seconds(-1.5)), Is.Zero);
    }

    [Test]
    public void OffsetPreservesLargeIntegerDeltas()
    {
        const long start = 9_007_199_254_740_992;
        var property = MakeProperty(0L, "LoopOut(LoopMode.Offset)", TestAnimations.Linear(start, start + 1, Seconds(1)));

        Assert.That(Evaluate(property, Seconds(2)), Is.EqualTo(start + 2));
    }

    [Test]
    public void ScaledOffsetKeepsLargeIntegerValuesExact()
    {
        const long start = 9_007_199_254_740_992;

        // 3 + 3 * 0.75 = 5.25 past the start, beyond double's integer precision.
        Assert.That(NumericOffset.AddScaled(start + 3, start, start + 3, 0.75), Is.EqualTo(start + 5));
    }

    [TestCase(1L, 1.5, 2L)]
    [TestCase(2L, 1.5, 4L)]
    [TestCase(1L, -1.5, 0L)]
    public void ScaledOffsetRoundsLargeIntegersHalfToEven(long offset, double scale, long expected)
    {
        // start is even, so start + 2.5 rounds to start + 2 and start + 3.5 to start + 4.
        const long start = 9_007_199_254_740_992;

        Assert.That(NumericOffset.AddScaled(start + offset, start, start + 1, scale), Is.EqualTo(start + expected));
    }

    [Test]
    public void ContinueKeepsLargeIntegerComponentsExact([Values] bool separated)
    {
        const int start = 1 << 25;
        var property = MakeProperty(default(PixelPoint), "LoopOut(LoopMode.Continue)", MakeAnimation(
            (0, new PixelPoint(start, 0)), (1, new PixelPoint(start + 3, 0))));
        if (separated)
            ComponentAnimations.Separate(property);

        Assert.That(Evaluate(property, Seconds(2)).X, Is.EqualTo(start + 6));
    }

    [TestCase("LoopOut(LoopMode.Offset)", 1.5, 55)]
    [TestCase("LoopIn(LoopMode.Offset)", -0.5, -45)]
    public void OffsetCarriesAJumpAtTheOuterBoundary(string script, double time, double expected)
    {
        var animation = script.StartsWith("LoopOut")
            ? MakeAnimation((0, 0.0), (1, 10.0), (1, 50.0))
            : MakeAnimation((0, -40.0), (0, 0.0), (1, 10.0));
        var property = MakeProperty(0.0, script, animation);

        Assert.That(Evaluate(property, Seconds(time)), Is.EqualTo(expected));
    }

    [Test]
    public void ScaledOffsetWidensFloatIntermediates()
    {
        Assert.That(NumericOffset.AddScaled(1e38f, -3e38f, 1e38f, 0.25), Is.EqualTo(2e38f).Within(1e32f));
    }

    [Test]
    public void ScaledOffsetKeepsDecimalDigits()
    {
        Assert.That(NumericOffset.AddScaled(10000000000000000.1m, 10000000000000000.1m, 10000000000000000.2m, 2),
            Is.EqualTo(10000000000000000.3m));
    }

    [Test]
    public void ContinueKeepsLargeIntegerValuesExact()
    {
        const long start = 9_007_199_254_740_992;
        var animation = new KeyFrameAnimation<long>();
        animation.KeyFrames.Add(new KeyFrame<long> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = start }, Easing = { Value = new LinearEasing() } });
        animation.KeyFrames.Add(new KeyFrame<long> { KeyTime = { Value = new MediaTime(4) }, Value = { Value = start + 3 }, Easing = { Value = new LinearEasing() } });
        var property = MakeProperty(0L, "LoopOut(LoopMode.Continue)", animation);

        Assert.That(Evaluate(property, new MediaTime(7)), Is.EqualTo(start + 5));
    }

    [Test]
    public void ContinueSaturatesBoundedValues()
    {
        var number = MakeProperty((byte)0, "LoopOut(LoopMode.Continue)", TestAnimations.Linear((byte)0, (byte)200, Seconds(1)));
        Color from = Color.FromArgb(0, 10, 20, 30);
        Color to = Color.FromArgb(128, 10, 20, 30);
        var color = MakeProperty(from, "LoopOut(LoopMode.Continue)", TestAnimations.Linear(from, to, Seconds(1)));

        Assert.That(Evaluate(number, Seconds(2)), Is.EqualTo(255));
        Assert.That(Evaluate(color, Seconds(2)).A, Is.EqualTo(255));
    }

    [Test]
    public void ContinueSaturatesDecimals()
    {
        var property = MakeProperty(0m, "LoopOut(LoopMode.Continue)", TestAnimations.Linear(0m, 5e28m, Seconds(1)));

        Assert.That(Evaluate(property, Seconds(2)), Is.EqualTo(decimal.MaxValue));
    }


    [Test]
    public void ContinueKeepsASteepContinuousSplineMoving()
    {
        var animation = TestAnimations.Linear(0.0, 100.0, Seconds(1));
        animation.KeyFrames[^1].Easing.Value = new SplineEasing(0, 0, 0.999f, 0);
        var property = MakeProperty(0.0, "LoopOut(LoopMode.Continue)", animation);

        Assert.That(Evaluate(property, Seconds(2)), Is.GreaterThan(100));
    }

    [Test]
    public void ContinueHoldsTheValueAfterAJumpAtAFlatBoundary()
    {
        var animation = MakeAnimation((0, 0.0), (1, 10.0), (1, 50.0));
        animation.KeyFrames[1].Easing.Value = new CubicEaseOut();
        var property = MakeProperty(0.0, "LoopOut(LoopMode.Continue)", animation);

        Assert.That(Evaluate(property, Seconds(2)), Is.EqualTo(50));
    }

    [TestCase("LoopOut(LoopMode.Continue)", 2.0)]
    [TestCase("LoopIn(LoopMode.Continue)", -1.0)]
    public void ContinueHoldsAfterAHoldEasing(string script, double time)
    {
        var animation = TestAnimations.Linear(0.0, 100.0, Seconds(1));
        animation.KeyFrames[^1].Easing.Value = new HoldEasing();
        var property = MakeProperty(0.0, script, animation);

        Assert.That(Evaluate(property, Seconds(time)), Is.EqualTo(script.StartsWith("LoopOut") ? 100 : 0));
    }

    [Test]
    public void ContinueFollowsTheColorAnimator()
    {
        Color from = Color.FromArgb(255, 0, 0, 0);
        Color to = Color.FromArgb(255, 128, 128, 128);
        var property = MakeProperty(from, "LoopOut(LoopMode.Continue)", TestAnimations.Linear(from, to, Seconds(1)));

        Assert.That(Evaluate(property, Seconds(1.5)), Is.EqualTo(new ColorAnimator().Interpolate(1.5f, from, to)));
    }

    [Test]
    public void ContinueHoldsAcrossAUnitChangeInSeparatedComponents()
    {
        var from = new RelativePoint(20, 40, RelativeUnit.Absolute);
        var to = new RelativePoint(0.2f, 0.4f, RelativeUnit.Relative);
        var property = MakeProperty(from, "LoopOut(LoopMode.Continue)", TestAnimations.Linear(from, to, Seconds(1)));
        ComponentAnimations.Separate(property);

        Assert.That(Evaluate(property, Seconds(2)), Is.EqualTo(to));
    }

    [Test]
    public void OffsetUsesTheTargetValueWhenTheUnitChanges()
    {
        var from = new RelativePoint(20, 40, RelativeUnit.Absolute);
        var to = new RelativePoint(0.2f, 0.4f, RelativeUnit.Relative);
        var property = MakeProperty(from, "LoopOut(LoopMode.Offset)", TestAnimations.Linear(from, to, Seconds(1)));

        Assert.That(Evaluate(property, Seconds(2.25)), Is.EqualTo(to));
    }

    private static MediaTime Seconds(double seconds) => MediaTime.FromSeconds(seconds);

    private static EngineObject MakeOwner()
        => new AnimationSamplerTestRoot { TimeRange = { Value = new TimeRange(Seconds(5), Seconds(10)) } };

    private static KeyFrameAnimation<T> MakeAnimation<T>(params (double Time, T Value)[] keys)
    {
        var animation = new KeyFrameAnimation<T>();
        foreach (var (time, value) in keys)
        {
            animation.KeyFrames.Add(new KeyFrame<T>
            {
                KeyTime = { Value = Seconds(time) },
                Value = { Value = value },
                Easing = { Value = new LinearEasing() }
            });
        }
        return animation;
    }

    private static AnimatableProperty<T> MakeProperty<T>(T value, string script, KeyFrameAnimation<T>? animation = null, EngineObject? owner = null)
    {
        var property = new AnimatableProperty<T>(value);
        property.SetAttributes("Value", []);
        property.SetOwnerObject(owner);
        property.SetKeyFrameAnimation(animation);
        property.Expression = Expression.Create<T>(script);
        return property;
    }

    private static T Evaluate<T>(IExpressionProperty<T> property, MediaTime time)
    {
        T value = property.Evaluate(new CompositionContext(time));
        Assert.That(property.ExpressionError, Is.Null);
        return value;
    }
}
