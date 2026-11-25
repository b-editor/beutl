using System.Numerics;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Engine.Expressions;

[TestFixture]
public class ExpressionWiggleTests
{
    private static readonly Guid s_ownerId = new("01234567-89ab-cdef-0123-456789abcdef");

    [Test]
    public void Wiggle_DefaultSeedIsStableForGuidAndUtf8Name()
    {
        const string propertyName = "Position_位置";
        const ulong expectedSeed = 0x8D77A2440C81384FUL;
        var owner = new TestEngineObject { Id = { Value = s_ownerId } };
        var property = MakeProperty(owner, propertyName, 0.0);
        var globals = At(0.375, property);
        double expected = WiggleNoise.Sample(0.75, 3, expectedSeed) * 10;

        Assert.That(WiggleNoise.CreateSeed(s_ownerId, propertyName), Is.EqualTo(expectedSeed));
        Assert.That(globals.Wiggle(2, 10, 3), Is.EqualTo(expected));
    }

    [Test]
    public void Wiggle_ReevaluatingTheSameFrameReturnsTheSameBits()
    {
        var owner = new TestEngineObject { Id = { Value = s_ownerId } };
        var globals = At(0.375, owner.Value);
        long bits = BitConverter.DoubleToInt64Bits(globals.Wiggle(2, 10, 3));
        _ = At(0.75, owner.Value).Wiggle(2, 10, 3);

        Assert.That(BitConverter.DoubleToInt64Bits(globals.Wiggle(2, 10, 3)), Is.EqualTo(bits));
        Assert.That(BitConverter.DoubleToInt64Bits(At(0.375, owner.Value).Wiggle(2, 10, 3)), Is.EqualTo(bits));
    }

    [Test]
    public void Wiggle_DefaultSeedUsesBothPropertyNameAndOwnerId()
    {
        var owner = new TestEngineObject { Id = { Value = s_ownerId } };
        var otherOwner = new TestEngineObject { Id = { Value = new Guid("01234567-89ab-cdef-0123-456789abcdee") } };
        var otherProperty = MakeProperty(owner, "OtherValue", 0.0);
        double value = At(0.375, owner.Value).Wiggle(2, 10);

        Assert.That(At(0.375, otherProperty).Wiggle(2, 10), Is.Not.EqualTo(value));
        Assert.That(At(0.375, otherOwner.Value).Wiggle(2, 10), Is.Not.EqualTo(value));
    }

    [Test]
    public void Wiggle_ExplicitSeedOverridesPropertyIdentity()
    {
        var owner = new TestEngineObject { Id = { Value = s_ownerId } };
        var otherOwner = new TestEngineObject { Id = { Value = new Guid("01234567-89ab-cdef-0123-456789abcdee") } };
        var first = At(0.375, owner.Value);
        var second = At(0.375, MakeProperty(otherOwner, "OtherValue", 0.0));

        long value = BitConverter.DoubleToInt64Bits(first.Wiggle(2, 10, 3, seed: 42));
        Assert.That(BitConverter.DoubleToInt64Bits(second.Wiggle(2, 10, 3, seed: 42)), Is.EqualTo(value));
        Assert.That(BitConverter.DoubleToInt64Bits(second.Wiggle(2, 10, 3, seed: -42)), Is.Not.EqualTo(value));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Wiggle_WorksWithUninitializedProperties(bool animatable)
    {
        var property = animatable ? Property.CreateAnimatable(0.0) : Property.CreateExpression(0.0);
        double value = At(0.375, property).Wiggle(2, 10);

        Assert.That(value, Is.InRange(-10.0, 10.0));
    }

    [TestCase(0.25, 1)]
    [TestCase(2.0, 1)]
    [TestCase(2.0, 3)]
    [TestCase(12.5, 16)]
    [TestCase(-2.75, 3)]
    public void Wiggle_VariesWithinAmplitude(double frequency, int octaves)
    {
        var context = TestHelper.CreateExpressionContext(MediaTime.Zero);
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;

        for (int i = -128; i <= 128; i++)
        {
            var globals = new ExpressionGlobals(context.WithTime(MediaTime.FromSeconds(i / 32.0)));
            double value = globals.Wiggle(frequency, 10, octaves, seed: 42);
            Assert.That(value, Is.InRange(-10.0, 10.0));
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        Assert.That(max - min, Is.GreaterThan(1.0));
    }

    [Test]
    public void Wiggle_DoesNotRepeatAtIntegerCoordinates()
    {
        var property = Property.CreateExpression(0.0);
        double origin = At(0, property).Wiggle(1, 10, seed: 42);

        Assert.That(At(1, property).Wiggle(1, 10, seed: 42), Is.Not.EqualTo(origin));
        Assert.That(At(256, property).Wiggle(1, 10, seed: 42), Is.Not.EqualTo(origin));
    }

    [TestCase(-0.5)]
    [TestCase(0.0)]
    [TestCase(0.5)]
    public void Wiggle_ChangesSmoothlyAcrossLatticeBoundaries(double time)
    {
        const double dt = 0.00001;
        var property = Property.CreateExpression(0.0);
        double before = At(time - dt, property).Wiggle(2, 10, 3, seed: 42);
        double atBoundary = At(time, property).Wiggle(2, 10, 3, seed: 42);
        double after = At(time + dt, property).Wiggle(2, 10, 3, seed: 42);

        Assert.That(Math.Abs(after - before), Is.LessThan(0.01));
        Assert.That(after - atBoundary, Is.EqualTo(atBoundary - before).Within(1e-6));
    }

    [Test]
    public void Wiggle_HigherFrequencyChangesFaster()
    {
        var context = TestHelper.CreateExpressionContext(MediaTime.Zero);

        double TotalChange(double frequency)
        {
            double change = 0;
            double previous = new ExpressionGlobals(context).Wiggle(frequency, 10, seed: 42);
            for (int i = 1; i <= 400; i++)
            {
                var globals = new ExpressionGlobals(context.WithTime(MediaTime.FromSeconds(i / 100.0)));
                double current = globals.Wiggle(frequency, 10, seed: 42);
                change += Math.Abs(current - previous);
                previous = current;
            }

            return change;
        }

        Assert.That(TotalChange(8), Is.GreaterThan(TotalChange(0.5)));
    }

    [Test]
    public void Wiggle_AddsOctavesAndCapsTheirCount()
    {
        var globals = At(0.375, Property.CreateExpression(0.0));
        double firstOctave = globals.Wiggle(2, 10, seed: 42);
        double manyOctaves = globals.Wiggle(2, 10, 100000, seed: 42);

        Assert.That(globals.Wiggle(2, 10, 1, seed: 42), Is.EqualTo(firstOctave));
        Assert.That(globals.Wiggle(2, 10, 3, seed: 42), Is.Not.EqualTo(firstOctave));
        Assert.That(manyOctaves, Is.EqualTo(globals.Wiggle(2, 10, 16, seed: 42)));
        Assert.That(manyOctaves, Is.InRange(-10.0, 10.0));
    }

    [TestCase(0.0, 1)]
    [TestCase(10.0, 0)]
    [TestCase(10.0, -2)]
    public void Wiggle_ReturnsZeroWithNoAmplitudeOrOctaves(double amplitude, int octaves)
    {
        var globals = At(0.375, Property.CreateExpression(0.0));
        Assert.That(globals.Wiggle(2, amplitude, octaves), Is.Zero);
    }

    [TestCase(0)]
    [TestCase(-2)]
    public void WiggleNoise_SampleReturnsZeroForNonPositiveOctaves(int octaves)
    {
        Assert.That(WiggleNoise.Sample(0.75, octaves, 42UL), Is.Zero);
        Assert.That(WiggleNoise.Sample(double.NaN, octaves, 42UL), Is.NaN);
    }

    [Test]
    public void Wiggle_NegativeAmplitudeInvertsTheOffset()
    {
        var globals = At(-1.375, Property.CreateExpression(0.0));
        double value = globals.Wiggle(2, -10, 3, seed: 42);

        Assert.That(value, Is.InRange(-10.0, 10.0));
        Assert.That(value, Is.EqualTo(-globals.Wiggle(2, 10, 3, seed: 42)));
    }

    [Test]
    public void Wiggle_ZeroFrequencySamplesTheOriginContinuously()
    {
        var property = Property.CreateExpression(0.0);
        var globals = At(-3.5, property);
        double value = globals.Wiggle(0, 10, 3, seed: 42);

        Assert.That(value, Is.EqualTo(At(0, property).Wiggle(2, 10, 3, seed: 42)));
        Assert.That(globals.Wiggle(1e-9, 10, 3, seed: 42), Is.EqualTo(value).Within(1e-6));
        Assert.That(globals.Wiggle(-1e-9, 10, 3, seed: 42), Is.EqualTo(value).Within(1e-6));
    }

    [TestCase(1e30, 16)]
    [TestCase(-1e30, 16)]
    [TestCase(double.MaxValue, 1)]
    [TestCase(double.MinValue, 1)]
    public void Wiggle_HandlesHugeFiniteCoordinates(double frequency, int octaves)
    {
        var globals = At(1, Property.CreateExpression(0.0));
        double value = globals.Wiggle(frequency, 10, octaves, seed: 42);
        double neighbor = Math.CopySign(Math.BitDecrement(Math.Abs(frequency)), frequency);

        Assert.That(value, Is.InRange(-10.0, 10.0));
        Assert.That(globals.Wiggle(neighbor, 10, octaves, seed: 42), Is.Not.EqualTo(value));
    }

    [TestCase(2.0, 1)]
    [TestCase(1.0, 2)]
    public void Wiggle_ReturnsNaNWhenSampleCoordinateOverflows(double time, int octaves)
    {
        var globals = At(time, Property.CreateExpression(0.0));
        Assert.That(globals.Wiggle(double.MaxValue, 10, octaves, seed: 42), Is.NaN);
    }

    [TestCase(32.0, false)]
    [TestCase(32.0, true)]
    [TestCase(32f, false)]
    [TestCase(32f, true)]
    [TestCase(32, false)]
    [TestCase(32, true)]
    public void StringExpression_AddsWiggleToNumericValue<T>(T stored, bool hasKeyFrames)
    {
        var property = new AnimatableProperty<T>(stored);
        property.SetAttributes("Value", []);
        if (hasKeyFrames)
            property.SetKeyFrameAnimation(TestAnimations.Linear(default(T)!, stored, MediaTime.FromSeconds(1)));
        property.Expression = new StringExpression<T>("Value + Wiggle(2, 10)");

        const double time = 0.375;
        double baseline = hasKeyFrames ? 12 : 32;
        double expected = baseline + At(time, property).Wiggle(2, 10);
        T value = property.Evaluate(new CompositionContext(MediaTime.FromSeconds(time)));

        Assert.That(value, Is.EqualTo((T)Convert.ChangeType(expected, typeof(T))));
        Assert.That(property.ExpressionError, Is.Null);
    }

    [Test]
    public void StringExpression_WiggleUsesOwnerLocalTimeAndAcceptsNamedSeed()
    {
        var owner = new TestEngineObject
        {
            TimeRange = { Value = new TimeRange(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2)) }
        };
        owner.Value.Expression = new StringExpression<double>("Wiggle(2, 10, seed: 42)");
        double expected = At(0.375, Property.CreateExpression(0.0)).Wiggle(2, 10, seed: 42);
        double value = owner.Value.Evaluate(new CompositionContext(MediaTime.FromSeconds(5.375)));

        Assert.That(value, Is.EqualTo(expected));
        Assert.That(owner.Value.ExpressionError, Is.Null);
    }

    [TestCase("double.NaN, 10", false)]
    [TestCase("2, double.PositiveInfinity", true)]
    public void StringExpression_NonFiniteWiggleArgumentsReportErrorAndFallBack(string arguments, bool hasKeyFrames)
    {
        var property = new AnimatableProperty<double>(32);
        property.SetAttributes("Value", []);
        if (hasKeyFrames)
            property.SetKeyFrameAnimation(TestAnimations.Linear(0.0, 32.0, MediaTime.FromSeconds(1)));
        property.Expression = new StringExpression<double>($"Value + Wiggle({arguments})");

        double value = property.Evaluate(new CompositionContext(MediaTime.FromSeconds(0.375)));

        Assert.That(value, Is.EqualTo(hasKeyFrames ? 12 : 32));
        Assert.That(property.ExpressionError, Does.Contain("finite number"));
    }

    [TestCase(null)]
    [TestCase(-42)]
    public void WiggleValue_ScalarAddsTheSameOffsetAsWiggle(int? seed)
    {
        var globals = AtValue(0.375, Property.CreateExpression(32.0));

        Assert.That(globals.WiggleValue(2, 10, 3, seed), Is.EqualTo(globals.Value + globals.Wiggle(2, 10, 3, seed)));
    }

    [Test]
    public void WiggleValue_VectorComponentsUseStableIndependentSeeds()
    {
        const float amplitude = 10;
        var baseline = new Vector3(32, -10, 5);
        var globals = AtValue(0.375, Property.CreateExpression(baseline));

        Vector3 result = globals.WiggleValue(2, amplitude, 3, seed: 42);
        Vector3 offset = result - baseline;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(offset.X, Is.InRange(-amplitude, amplitude));
            Assert.That(offset.Y, Is.InRange(-amplitude, amplitude));
            Assert.That(offset.Z, Is.InRange(-amplitude, amplitude));
            Assert.That(new[] { offset.X, offset.Y, offset.Z }, Is.Unique);
            Assert.That(result, Is.EqualTo(new Vector3(
                (float)(baseline.X + globals.Wiggle(2, amplitude, 3, seed: 42)),
                (float)(baseline.Y + WiggleNoise.Sample(0.75, 3, 0x910A2DEC89025CEBUL) * amplitude),
                (float)(baseline.Z + WiggleNoise.Sample(0.75, 3, 0x975835DE1C9756E4UL) * amplitude))));
        }
    }

    [Test]
    public void WiggleValue_PointMovesEachComponentWithinAmplitude()
    {
        var baseline = new Point(8, -8);
        var property = Property.CreateExpression(baseline);
        Point first = AtValue(0.375, property).WiggleValue(2, 10, 3, seed: 42);
        Point second = AtValue(0.625, property).WiggleValue(2, 10, 3, seed: 42);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.X - baseline.X, Is.InRange(-10f, 10f));
            Assert.That(first.Y - baseline.Y, Is.InRange(-10f, 10f));
            Assert.That(first.X - baseline.X, Is.Not.EqualTo(first.Y - baseline.Y).Within(1e-5f));
            Assert.That(second.X, Is.Not.EqualTo(first.X));
            Assert.That(second.Y, Is.Not.EqualTo(first.Y));
        }
    }

    [Test]
    public void WiggleValue_ReevaluatingAFrameReturnsTheSameBits()
    {
        var property = MakeProperty(new TestEngineObject { Id = { Value = s_ownerId } }, "Position", Vector3.One);

        var first = AtValue(0.375, property).WiggleValue(2, 10, 3);
        _ = AtValue(0.625, property).WiggleValue(2, 10, 3);
        var repeated = AtValue(0.375, property).WiggleValue(2, 10, 3);

        static (int, int, int) Bits(Vector3 value) =>
            (BitConverter.SingleToInt32Bits(value.X), BitConverter.SingleToInt32Bits(value.Y), BitConverter.SingleToInt32Bits(value.Z));

        Assert.That(Bits(repeated), Is.EqualTo(Bits(first)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void StringExpression_WiggleValueSeedsFromIdentityUnlessExplicit(bool explicitSeed)
    {
        var owner = new TestEngineObject { Id = { Value = s_ownerId } };
        var otherOwner = new TestEngineObject { Id = { Value = new Guid("01234567-89ab-cdef-0123-456789abcdee") } };
        var first = MakeProperty(owner, "Position", Vector3.One);
        var second = MakeProperty(otherOwner, "Position", Vector3.One);
        var expression = new StringExpression<Vector3>(explicitSeed ? "WiggleValue(2, 10, 3, seed: -42)" : "WiggleValue(2, 10, 3)");
        first.Expression = second.Expression = expression;

        var context = new CompositionContext(MediaTime.FromSeconds(0.375));
        Vector3 firstValue = first.Evaluate(context);
        Vector3 secondValue = second.Evaluate(context);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.ExpressionError, Is.Null);
            Assert.That(second.ExpressionError, Is.Null);
            Assert.That(secondValue, explicitSeed ? Is.EqualTo(firstValue) : Is.Not.EqualTo(firstValue));
        }
    }

    [Test]
    public void WiggleValue_RoundsAndSaturatesIntegerComponents()
    {
        var globals = AtValue(0.375, Property.CreateExpression(new PixelPoint(10, -10)));
        double amplitude = 0.75 / globals.Wiggle(2, 1, seed: 42);
        var upper = AtValue(0.375, Property.CreateExpression(new PixelPoint(int.MaxValue, 0)));
        var lower = AtValue(0.375, Property.CreateExpression(new PixelPoint(int.MinValue, 0)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(globals.WiggleValue(2, amplitude, seed: 42).X, Is.EqualTo(11));
            Assert.That(globals.WiggleValue(2, -amplitude, seed: 42).X, Is.EqualTo(9));
            Assert.That(upper.WiggleValue(2, amplitude * 4, seed: 42).X, Is.EqualTo(int.MaxValue));
            Assert.That(lower.WiggleValue(2, -amplitude * 4, seed: 42).X, Is.EqualTo(int.MinValue));
        }
    }

    [Test]
    public void WiggleValue_PreservesPrecisionOfLargeIntegralValues()
    {
        const long baseline = long.MaxValue - 5;
        var globals = AtValue(0.375, Property.CreateExpression(baseline));
        double amplitude = 0.75 / globals.Wiggle(2, 1, seed: 42);

        Assert.That(globals.WiggleValue(2, amplitude, seed: 42), Is.EqualTo(baseline + 1));
    }

    [Test]
    public void WiggleValue_ColorRoundsAndClampsRgbAndPreservesAlpha()
    {
        var middle = AtValue(0.375, Property.CreateExpression(Color.FromArgb(127, 100, 100, 100)));
        var edges = AtValue(0.375, Property.CreateExpression(Color.FromArgb(127, 254, 100, 0)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(middle.WiggleValue(2, 6.5, 3, seed: 42), Is.EqualTo(Color.FromArgb(127, 103, 102, 99)));
            Assert.That(edges.WiggleValue(2, 6.5, 3, seed: 42), Is.EqualTo(Color.FromArgb(127, 255, 102, 0)));
        }
    }

    [Test]
    public void WiggleValue_RelativePointKeepsItsUnit()
    {
        var baseline = new RelativePoint(0.5f, 0.25f, RelativeUnit.Relative);
        var globals = AtValue(0.375, Property.CreateExpression(baseline));
        var point = AtValue(0.375, Property.CreateExpression(baseline.Point));

        RelativePoint result = globals.WiggleValue(2, 0.1, 3, seed: 42);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Unit, Is.EqualTo(RelativeUnit.Relative));
            Assert.That(result.Point, Is.EqualTo(point.WiggleValue(2, 0.1, 3, seed: 42)));
        }
    }

    [Test]
    public void WiggleValue_RejectsUnsupportedTypesEvenAtZeroAmplitude()
    {
        var globals = AtValue(0.375, Property.CreateExpression(true));

        var exception = Assert.Throws<ExpressionException>(() => globals.WiggleValue(2, 0));
        Assert.That(exception?.Message, Does.Contain("WiggleValue").And.Contain("Boolean"));
    }

    [TestCase(0.0, 1)]
    [TestCase(10.0, 0)]
    [TestCase(10.0, -2)]
    public void WiggleValue_PreservesTheExactValueWithNoAmplitudeOrOctaves(double amplitude, int octaves)
    {
        var globals = AtValue(0.375, Property.CreateExpression(BitConverter.Int64BitsToDouble(long.MinValue)));

        Assert.That(BitConverter.DoubleToInt64Bits(globals.WiggleValue(2, amplitude, octaves, seed: 42)), Is.EqualTo(long.MinValue));
    }

    [Test]
    public void StringExpression_WiggleValueOffsetsTheInterpolatedValue()
    {
        var property = new AnimatableProperty<Vector3>(new Vector3(400, -400, 700));
        property.SetAttributes("Position", []);
        property.SetKeyFrameAnimation(TestAnimations.Linear(new Vector3(8, 16, 32), new Vector3(24, 48, 64), MediaTime.FromSeconds(1)));
        property.Expression = new StringExpression<Vector3>("WiggleValue(2, 10, 3, seed: 42)");

        const double time = 0.375;
        var baseline = Property.CreateExpression(new Vector3(14, 28, 44));
        Vector3 expected = AtValue(time, baseline).WiggleValue(2, 10, 3, seed: 42);
        Vector3 result = property.Evaluate(new CompositionContext(MediaTime.FromSeconds(time)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(property.ExpressionError, Is.Null);
            Assert.That(result, Is.EqualTo(expected));
        }
    }

    [TestCase("double.NaN, 0, 0")]
    [TestCase("2, double.PositiveInfinity")]
    [TestCase("double.MaxValue, 10, 4")]
    public void StringExpression_WiggleValueNonFiniteOffsetsReportErrorAndFallBack(string arguments)
    {
        const double time = 0.375;
        var context = new CompositionContext(MediaTime.FromSeconds(time));
        var scalar = new AnimatableProperty<double>(32);
        scalar.SetAttributes("Value", []);
        scalar.Expression = new StringExpression<double>($"WiggleValue({arguments})");
        var point = new AnimatableProperty<Point>(new Point(100, 200));
        point.SetAttributes("Position", []);
        point.SetKeyFrameAnimation(TestAnimations.Linear(new Point(0, 0), new Point(32, 64), MediaTime.FromSeconds(1)));
        point.Expression = new StringExpression<Point>($"WiggleValue({arguments})");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scalar.Evaluate(context), Is.EqualTo(32.0));
            Assert.That(scalar.ExpressionError ?? string.Empty, Does.Contain("finite number"));
            Assert.That(point.Evaluate(context), Is.EqualTo(new Point(12, 24)));
            Assert.That(point.ExpressionError ?? string.Empty, Does.Contain("finite number"));
        }
    }

    [Test]
    public void StringExpression_WiggleValueReportsANonFiniteOffsetTheExpressionDoesNotReturn()
    {
        var context = new CompositionContext(MediaTime.FromSeconds(0.375));
        var scalar = new AnimatableProperty<double>(32);
        scalar.SetAttributes("Value", []);
        // The comparison discards the NaN, so the property's check on the result never sees it.
        scalar.Expression = new StringExpression<double>("WiggleValue(double.NaN, 10) > 0 ? 1.0 : 2.0");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scalar.Evaluate(context), Is.EqualTo(32.0));
            Assert.That(scalar.ExpressionError ?? string.Empty, Does.Contain("finite number"));
        }
    }

    [Test]
    public void ComponentExpression_WiggleValueParsesAnEditInTheCombinedContext()
    {
        var transform = new TranslateTransform(10, 20);
        const string script = "WiggleValue(2, 10, 3, seed: 42)";
        Assert.That(ComponentExpression<Point, float>.TryParse(script, out var expressions, out var error), Is.True, error);
        transform.X.Expression = expressions![0];
        transform.Y.Expression = expressions[1];
        var context = new CompositionContext(MediaTime.FromSeconds(0.375));
        float previous = transform.Y.Evaluate(context);
        Assert.That(transform.Y.ExpressionError, Is.Null);

        var combined = (ICombinedValueExpression<float>)expressions[1];
        Assert.That(combined.TryParseSameContext($"{script}.Y + 1", out var edited, out error), Is.True, error);
        Assert.That(edited, Is.TypeOf<CombinedValueExpression<Point, float>>());
        transform.Y.Expression = edited;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(transform.Y.Evaluate(context), Is.EqualTo(previous + 1));
            Assert.That(transform.Y.ExpressionError, Is.Null);
        }
    }

    private static ExpressionGlobals At(double seconds, IProperty property)
        => new(TestHelper.CreateExpressionContext(MediaTime.FromSeconds(seconds), property));

    private static ExpressionGlobals<T> AtValue<T>(double seconds, IProperty<T> property)
        => new(TestHelper.CreateExpressionContext(MediaTime.FromSeconds(seconds), property), null);

    private static IExpressionProperty<T> MakeProperty<T>(EngineObject owner, string name, T value)
    {
        var property = Property.CreateExpression(value);
        property.SetAttributes(name, []);
        property.SetOwnerObject(owner);
        return property;
    }
}
