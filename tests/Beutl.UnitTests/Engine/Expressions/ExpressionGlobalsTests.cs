using Beutl.Engine;
using Beutl.Engine.Expressions;

namespace Beutl.UnitTests.Engine.Expressions;

[TestFixture]
public class ExpressionGlobalsTests
{
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
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var globals = new ExpressionGlobals(context);

        // Assert
        Assert.That(globals.PI, Is.EqualTo(Math.PI));
    }

    [Test]
    public void Sin_ShouldCalculateCorrectly()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var globals = new ExpressionGlobals(context);

        // Assert
        Assert.That(globals.Sin(0.0), Is.EqualTo(0.0).Within(0.0001));
        Assert.That(globals.Sin(Math.PI / 2), Is.EqualTo(1.0).Within(0.0001));
    }

    [Test]
    public void Cos_ShouldCalculateCorrectly()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var globals = new ExpressionGlobals(context);

        // Assert
        Assert.That(globals.Cos(0.0), Is.EqualTo(1.0).Within(0.0001));
        Assert.That(globals.Cos(Math.PI), Is.EqualTo(-1.0).Within(0.0001));
    }

    [Test]
    public void Sqrt_ShouldCalculateCorrectly()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var globals = new ExpressionGlobals(context);

        // Assert
        Assert.That(globals.Sqrt(4.0), Is.EqualTo(2.0).Within(0.0001));
        Assert.That(globals.Sqrt(9.0), Is.EqualTo(3.0).Within(0.0001));
    }

    [Test]
    public void Pow_ShouldCalculateCorrectly()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var globals = new ExpressionGlobals(context);

        // Assert
        Assert.That(globals.Pow(2.0, 3.0), Is.EqualTo(8.0).Within(0.0001));
        Assert.That(globals.Pow(3.0, 2.0), Is.EqualTo(9.0).Within(0.0001));
    }

    [Test]
    public void Abs_ShouldReturnAbsoluteValue()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var globals = new ExpressionGlobals(context);

        // Assert
        Assert.That(globals.Abs(-5.0), Is.EqualTo(5.0));
        Assert.That(globals.Abs(5.0), Is.EqualTo(5.0));
    }

    [Test]
    public void Min_ShouldReturnMinimumValue()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var globals = new ExpressionGlobals(context);

        // Assert
        Assert.That(globals.Min(3.0, 5.0), Is.EqualTo(3.0));
        Assert.That(globals.Min(5.0, 3.0), Is.EqualTo(3.0));
    }

    [Test]
    public void Max_ShouldReturnMaximumValue()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var globals = new ExpressionGlobals(context);

        // Assert
        Assert.That(globals.Max(3.0, 5.0), Is.EqualTo(5.0));
        Assert.That(globals.Max(5.0, 3.0), Is.EqualTo(5.0));
    }

    [Test]
    public void Clamp_ShouldClampValue()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var globals = new ExpressionGlobals(context);

        // Assert
        Assert.That(globals.Clamp(5.0, 0.0, 10.0), Is.EqualTo(5.0));
        Assert.That(globals.Clamp(-5.0, 0.0, 10.0), Is.EqualTo(0.0));
        Assert.That(globals.Clamp(15.0, 0.0, 10.0), Is.EqualTo(10.0));
    }

    [Test]
    public void Lerp_ShouldInterpolateCorrectly()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var globals = new ExpressionGlobals(context);

        // Assert
        Assert.That(globals.Lerp(0.0, 10.0, 0.0), Is.EqualTo(0.0).Within(0.0001));
        Assert.That(globals.Lerp(0.0, 10.0, 0.5), Is.EqualTo(5.0).Within(0.0001));
        Assert.That(globals.Lerp(0.0, 10.0, 1.0), Is.EqualTo(10.0).Within(0.0001));
    }

    [Test]
    public void InverseLerp_ShouldCalculateCorrectly()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var globals = new ExpressionGlobals(context);

        // Assert
        Assert.That(globals.InverseLerp(0.0, 10.0, 0.0), Is.EqualTo(0.0).Within(0.0001));
        Assert.That(globals.InverseLerp(0.0, 10.0, 5.0), Is.EqualTo(0.5).Within(0.0001));
        Assert.That(globals.InverseLerp(0.0, 10.0, 10.0), Is.EqualTo(1.0).Within(0.0001));
    }

    [Test]
    public void Remap_ShouldRemapValueCorrectly()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var globals = new ExpressionGlobals(context);

        // Assert (0-10 -> 0-100)
        Assert.That(globals.Remap(5.0, 0.0, 10.0, 0.0, 100.0), Is.EqualTo(50.0).Within(0.0001));
    }

    [Test]
    public void Smoothstep_ShouldCalculateCorrectly()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var globals = new ExpressionGlobals(context);

        // Assert
        Assert.That(globals.Smoothstep(0.0, 1.0, 0.0), Is.EqualTo(0.0).Within(0.0001));
        Assert.That(globals.Smoothstep(0.0, 1.0, 1.0), Is.EqualTo(1.0).Within(0.0001));
        Assert.That(globals.Smoothstep(0.0, 1.0, 0.5), Is.EqualTo(0.5).Within(0.0001));
    }

    [Test]
    public void Radians_ShouldConvertDegreesToRadians()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var globals = new ExpressionGlobals(context);

        // Assert
        Assert.That(globals.Radians(180.0), Is.EqualTo(Math.PI).Within(0.0001));
        Assert.That(globals.Radians(90.0), Is.EqualTo(Math.PI / 2).Within(0.0001));
    }

    [Test]
    public void Degrees_ShouldConvertRadiansToDegrees()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var globals = new ExpressionGlobals(context);

        // Assert
        Assert.That(globals.Degrees(Math.PI), Is.EqualTo(180.0).Within(0.0001));
        Assert.That(globals.Degrees(Math.PI / 2), Is.EqualTo(90.0).Within(0.0001));
    }

    [Test]
    public void Floor_ShouldFloorValue()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var globals = new ExpressionGlobals(context);

        // Assert
        Assert.That(globals.Floor(3.7), Is.EqualTo(3.0));
        Assert.That(globals.Floor(-3.7), Is.EqualTo(-4.0));
    }

    [Test]
    public void Ceil_ShouldCeilValue()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var globals = new ExpressionGlobals(context);

        // Assert
        Assert.That(globals.Ceil(3.2), Is.EqualTo(4.0));
        Assert.That(globals.Ceil(-3.2), Is.EqualTo(-3.0));
    }

    [Test]
    public void Round_ShouldRoundValue()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var globals = new ExpressionGlobals(context);

        // Assert
        Assert.That(globals.Round(3.4), Is.EqualTo(3.0));
        Assert.That(globals.Round(3.6), Is.EqualTo(4.0));
    }

    [Test]
    public void Mod_ShouldCalculateModulo()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var globals = new ExpressionGlobals(context);

        // Assert
        Assert.That(globals.Mod(5.0, 3.0), Is.EqualTo(2.0).Within(0.0001));
        Assert.That(globals.Mod(7.0, 4.0), Is.EqualTo(3.0).Within(0.0001));
    }

    [Test]
    public void Frac_ShouldReturnFractionalPart()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var globals = new ExpressionGlobals(context);

        // Assert
        Assert.That(globals.Frac(3.75), Is.EqualTo(0.75).Within(0.0001));
        Assert.That(globals.Frac(5.25), Is.EqualTo(0.25).Within(0.0001));
    }

    [Test]
    public void Random_WithSameSeed_ShouldReturnSameValue()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var globals = new ExpressionGlobals(context);

        // Act
        double value1 = globals.Random(42);
        double value2 = globals.Random(42);

        // Assert
        Assert.That(value1, Is.EqualTo(value2));
        Assert.That(value1, Is.GreaterThanOrEqualTo(0.0));
        Assert.That(value1, Is.LessThan(1.0));
    }

    [Test]
    public void Random_WithDifferentSeeds_ShouldReturnDifferentValues()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var globals = new ExpressionGlobals(context);

        // Act
        double value1 = globals.Random(1);
        double value2 = globals.Random(2);

        // Assert
        Assert.That(value1, Is.Not.EqualTo(value2));
    }

    [Test]
    public void Random_WithRange_ShouldReturnValueInRange()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var globals = new ExpressionGlobals(context);

        // Act
        double value = globals.Random(42, 10.0, 20.0);

        // Assert
        Assert.That(value, Is.GreaterThanOrEqualTo(10.0));
        Assert.That(value, Is.LessThan(20.0));
    }

    [Test]
    public void GetProperty_WithInvalidPath_ShouldReturnDefault()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var globals = new ExpressionGlobals(context);

        // Act
        double result = globals.GetProperty<double>("invalid-path");

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

    [Test]
    public void GetProperty_WithNonExistentObject_ShouldReturnDefault()
    {
        // Arrange
        var root = new TestEngineObject();
        var lookup = new PropertyLookup(root);
        var property = Property.Create(0.0);
        var context = new ExpressionContext(TimeSpan.Zero, property, lookup);
        var globals = new ExpressionGlobals(context);
        var nonExistentGuid = Guid.NewGuid();

        // Act
        double result = globals.GetProperty<double>($"{nonExistentGuid}.SomeProperty");

        // Assert
        Assert.That(result, Is.EqualTo(0.0));
    }
}
