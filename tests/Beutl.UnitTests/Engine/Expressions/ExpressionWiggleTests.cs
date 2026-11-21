using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Engine.Expressions;
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
        var property = MakeProperty(owner, propertyName);
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
        var otherProperty = MakeProperty(owner, "OtherValue");
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
        var second = At(0.375, MakeProperty(otherOwner, "OtherValue"));

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

    private static ExpressionGlobals At(double seconds, IProperty property)
        => new(TestHelper.CreateExpressionContext(MediaTime.FromSeconds(seconds), property));

    private static IExpressionProperty<double> MakeProperty(EngineObject owner, string name)
    {
        var property = Property.CreateExpression(0.0);
        property.SetAttributes(name, []);
        property.SetOwnerObject(owner);
        return property;
    }
}
