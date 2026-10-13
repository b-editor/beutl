using Beutl.Engine;
using Beutl.Engine.Expressions;

namespace Beutl.UnitTests.Engine.Expressions;

[TestFixture]
public class ExpressionGlobalsTests
{
    private ExpressionGlobals _globals = null!;

    [SetUp]
    public void SetUp()
    {
        _globals = new ExpressionGlobals(TestHelper.CreateExpressionContext(TimeSpan.Zero));
    }

    [Test]
    public void Constructor_WithNullContext_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ExpressionGlobals(null!));
    }

    [Test]
    public void Time_ShouldReturnContextTimeInSeconds()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.FromSeconds(5));
        var globals = new ExpressionGlobals(context);

        // Assert
        Assert.That(globals.Time, Is.EqualTo(5.0));
    }

    [Test]
    public void PI_ShouldReturnMathPI()
    {
        Assert.That(_globals.PI, Is.EqualTo(Math.PI));
    }

    // The function, its expected result, the tolerance (0 where the result is exact) and its arguments.
    [TestCase("Sin", 0.0, 1e-4, 0.0)]
    [TestCase("Sin", 1.0, 1e-4, Math.PI / 2)]
    [TestCase("Cos", 1.0, 1e-4, 0.0)]
    [TestCase("Cos", -1.0, 1e-4, Math.PI)]
    [TestCase("Sqrt", 2.0, 1e-4, 4.0)]
    [TestCase("Sqrt", 3.0, 1e-4, 9.0)]
    [TestCase("Pow", 8.0, 1e-4, 2.0, 3.0)]
    [TestCase("Pow", 9.0, 1e-4, 3.0, 2.0)]
    [TestCase("Abs", 5.0, 0.0, -5.0)]
    [TestCase("Abs", 5.0, 0.0, 5.0)]
    [TestCase("Min", 3.0, 0.0, 3.0, 5.0)]
    [TestCase("Min", 3.0, 0.0, 5.0, 3.0)]
    [TestCase("Max", 5.0, 0.0, 3.0, 5.0)]
    [TestCase("Max", 5.0, 0.0, 5.0, 3.0)]
    [TestCase("Clamp", 5.0, 0.0, 5.0, 0.0, 10.0)]
    [TestCase("Clamp", 0.0, 0.0, -5.0, 0.0, 10.0)]
    [TestCase("Clamp", 10.0, 0.0, 15.0, 0.0, 10.0)]
    [TestCase("Lerp", 0.0, 1e-4, 0.0, 10.0, 0.0)]
    [TestCase("Lerp", 5.0, 1e-4, 0.0, 10.0, 0.5)]
    [TestCase("Lerp", 10.0, 1e-4, 0.0, 10.0, 1.0)]
    [TestCase("InverseLerp", 0.0, 1e-4, 0.0, 10.0, 0.0)]
    [TestCase("InverseLerp", 0.5, 1e-4, 0.0, 10.0, 5.0)]
    [TestCase("InverseLerp", 1.0, 1e-4, 0.0, 10.0, 10.0)]
    [TestCase("Remap", 50.0, 1e-4, 5.0, 0.0, 10.0, 0.0, 100.0)]
    [TestCase("Smoothstep", 0.0, 1e-4, 0.0, 1.0, 0.0)]
    [TestCase("Smoothstep", 1.0, 1e-4, 0.0, 1.0, 1.0)]
    [TestCase("Smoothstep", 0.5, 1e-4, 0.0, 1.0, 0.5)]
    [TestCase("Radians", Math.PI, 1e-4, 180.0)]
    [TestCase("Radians", Math.PI / 2, 1e-4, 90.0)]
    [TestCase("Degrees", 180.0, 1e-4, Math.PI)]
    [TestCase("Degrees", 90.0, 1e-4, Math.PI / 2)]
    [TestCase("Floor", 3.0, 0.0, 3.7)]
    [TestCase("Floor", -4.0, 0.0, -3.7)]
    [TestCase("Ceil", 4.0, 0.0, 3.2)]
    [TestCase("Ceil", -3.0, 0.0, -3.2)]
    [TestCase("Round", 3.0, 0.0, 3.4)]
    [TestCase("Round", 4.0, 0.0, 3.6)]
    [TestCase("Mod", 2.0, 1e-4, 5.0, 3.0)]
    [TestCase("Mod", 3.0, 1e-4, 7.0, 4.0)]
    [TestCase("Frac", 0.75, 1e-4, 3.75)]
    [TestCase("Frac", 0.25, 1e-4, 5.25)]
    public void MathFunction_ShouldCalculateCorrectly(
        string function,
        double expected,
        double tolerance,
        params double[] arguments)
    {
        double actual = function switch
        {
            nameof(ExpressionGlobals.Sin) => _globals.Sin(arguments[0]),
            nameof(ExpressionGlobals.Cos) => _globals.Cos(arguments[0]),
            nameof(ExpressionGlobals.Sqrt) => _globals.Sqrt(arguments[0]),
            nameof(ExpressionGlobals.Pow) => _globals.Pow(arguments[0], arguments[1]),
            nameof(ExpressionGlobals.Abs) => _globals.Abs(arguments[0]),
            nameof(ExpressionGlobals.Min) => _globals.Min(arguments[0], arguments[1]),
            nameof(ExpressionGlobals.Max) => _globals.Max(arguments[0], arguments[1]),
            nameof(ExpressionGlobals.Clamp) => _globals.Clamp(arguments[0], arguments[1], arguments[2]),
            nameof(ExpressionGlobals.Lerp) => _globals.Lerp(arguments[0], arguments[1], arguments[2]),
            nameof(ExpressionGlobals.InverseLerp) => _globals.InverseLerp(arguments[0], arguments[1], arguments[2]),
            nameof(ExpressionGlobals.Remap) => _globals.Remap(
                arguments[0], arguments[1], arguments[2], arguments[3], arguments[4]),
            nameof(ExpressionGlobals.Smoothstep) => _globals.Smoothstep(arguments[0], arguments[1], arguments[2]),
            nameof(ExpressionGlobals.Radians) => _globals.Radians(arguments[0]),
            nameof(ExpressionGlobals.Degrees) => _globals.Degrees(arguments[0]),
            nameof(ExpressionGlobals.Floor) => _globals.Floor(arguments[0]),
            nameof(ExpressionGlobals.Ceil) => _globals.Ceil(arguments[0]),
            nameof(ExpressionGlobals.Round) => _globals.Round(arguments[0]),
            nameof(ExpressionGlobals.Mod) => _globals.Mod(arguments[0], arguments[1]),
            nameof(ExpressionGlobals.Frac) => _globals.Frac(arguments[0]),
            _ => throw new ArgumentOutOfRangeException(nameof(function), function, null),
        };

        Assert.That(actual, Is.EqualTo(expected).Within(tolerance));
    }

    [Test]
    public void Random_WithSameSeed_ShouldReturnSameValue()
    {
        // Act
        double value1 = _globals.Random(42);
        double value2 = _globals.Random(42);

        // Assert
        Assert.That(value1, Is.EqualTo(value2));
        Assert.That(value1, Is.GreaterThanOrEqualTo(0.0));
        Assert.That(value1, Is.LessThan(1.0));
    }

    [Test]
    public void Random_WithDifferentSeeds_ShouldReturnDifferentValues()
    {
        // Act
        double value1 = _globals.Random(1);
        double value2 = _globals.Random(2);

        // Assert
        Assert.That(value1, Is.Not.EqualTo(value2));
    }

    [Test]
    public void Random_WithRange_ShouldReturnValueInRange()
    {
        // Act
        double value = _globals.Random(42, 10.0, 20.0);

        // Assert
        Assert.That(value, Is.GreaterThanOrEqualTo(10.0));
        Assert.That(value, Is.LessThan(20.0));
    }

    [Test]
    public void GetProperty_WithInvalidPath_ShouldReturnDefault()
    {
        // Act
        double result = _globals.GetProperty<double>("invalid-path");

        // Assert
        Assert.That(result, Is.EqualTo(0.0));
    }

    [Test]
    public void GetProperty_WithValidPath_ShouldReturnPropertyValue()
    {
        // Arrange - Create a hierarchy with a property
        var root = new TestEngineObject();
        var child = new TestEngineObject();
        child.Value.CurrentValue = 42.0;
        root.AddChild(child);

        var lookup = new PropertyLookup(root);
        var property = Property.Create(0.0);
        var context = new ExpressionContext(TimeSpan.Zero, property, lookup);
        var globals = new ExpressionGlobals(context);

        // Act
        double result = globals.GetProperty<double>($"{child.Id}.Value");

        // Assert
        Assert.That(result, Is.EqualTo(42.0));
    }

    [Test]
    public void GetProperty_WithGuidOverload_ShouldReturnPropertyValue()
    {
        // Arrange - Create a hierarchy with a property
        var root = new TestEngineObject();
        var child = new TestEngineObject();
        child.Value.CurrentValue = 123.5;
        root.AddChild(child);

        var lookup = new PropertyLookup(root);
        var property = Property.Create(0.0);
        var context = new ExpressionContext(TimeSpan.Zero, property, lookup);
        var globals = new ExpressionGlobals(context);

        // Act - Using GUID + property name overload
        double result = globals.GetProperty<double>(child.Id, "Value");

        // Assert
        Assert.That(result, Is.EqualTo(123.5));
    }
}
