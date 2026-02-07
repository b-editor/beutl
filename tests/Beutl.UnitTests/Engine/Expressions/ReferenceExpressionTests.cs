using Beutl.Engine;
using Beutl.Engine.Expressions;

namespace Beutl.UnitTests.Engine.Expressions;

[TestFixture]
public class ReferenceExpressionTests
{
    [Test]
    public void Constructor_WithObjectIdOnly_ShouldSetCorrectProperties()
    {
        // Arrange
        var objectId = Guid.NewGuid();

        // Act
        var expression = new ReferenceExpression<TestEngineObject>(objectId);

        // Assert
        Assert.That(expression.ObjectId, Is.EqualTo(objectId));
        Assert.That(expression.PropertyPath, Is.EqualTo(string.Empty));
        Assert.That(expression.HasPropertyPath, Is.False);
    }

    [Test]
    public void Constructor_WithObjectIdAndPropertyPath_ShouldSetCorrectProperties()
    {
        // Arrange
        var objectId = Guid.NewGuid();
        const string propertyPath = "Value";

        // Act
        var expression = new ReferenceExpression<double>(objectId, propertyPath);

        // Assert
        Assert.That(expression.ObjectId, Is.EqualTo(objectId));
        Assert.That(expression.PropertyPath, Is.EqualTo(propertyPath));
        Assert.That(expression.HasPropertyPath, Is.True);
    }

    [Test]
    public void Constructor_WithNullPropertyPath_ShouldUseEmptyString()
    {
        // Arrange
        var objectId = Guid.NewGuid();

        // Act
        var expression = new ReferenceExpression<double>(objectId, null);

        // Assert
        Assert.That(expression.PropertyPath, Is.EqualTo(string.Empty));
        Assert.That(expression.HasPropertyPath, Is.False);
    }

    [Test]
    public void ExpressionString_WithoutPropertyPath_ShouldReturnObjectIdOnly()
    {
        // Arrange
        var objectId = Guid.NewGuid();
        var expression = new ReferenceExpression<TestEngineObject>(objectId);

        // Act
        string result = expression.ExpressionString;

        // Assert
        Assert.That(result, Is.EqualTo(objectId.ToString()));
    }

    [Test]
    public void ExpressionString_WithPropertyPath_ShouldReturnObjectIdAndPath()
    {
        // Arrange
        var objectId = Guid.NewGuid();
        const string propertyPath = "Value";
        var expression = new ReferenceExpression<double>(objectId, propertyPath);

        // Act
        string result = expression.ExpressionString;

        // Assert
        Assert.That(result, Is.EqualTo($"{objectId}.{propertyPath}"));
    }

    [Test]
    public void Validate_ShouldAlwaysReturnTrue()
    {
        // Arrange
        var expression = new ReferenceExpression<TestEngineObject>(Guid.NewGuid());

        // Act
        bool result = expression.Validate(out string? error);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(error, Is.Null);
    }

    [Test]
    public void ToString_ShouldReturnExpressionString()
    {
        // Arrange
        var objectId = Guid.NewGuid();
        var expression = new ReferenceExpression<TestEngineObject>(objectId);

        // Act
        string result = expression.ToString();

        // Assert
        Assert.That(result, Is.EqualTo(objectId.ToString()));
    }

    [Test]
    public void Evaluate_WhenObjectNotFound_ShouldReturnDefault()
    {
        // Arrange
        var objectId = Guid.NewGuid();
        var expression = new ReferenceExpression<TestEngineObject>(objectId);
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);

        // Act
        var result = expression.Evaluate(context);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Evaluate_WhenObjectFound_ShouldReturnObject()
    {
        // Arrange
        var root = new TestEngineObject();
        var target = new TestEngineObject();
        root.AddChild(target);
        var objectId = target.Id;
        var expression = new ReferenceExpression<TestEngineObject>(objectId);
        var propertyLookup = new PropertyLookup(root);
        var context = new ExpressionContext(TimeSpan.Zero, Property.Create(0.0), propertyLookup);

        // Act
        var result = expression.Evaluate(context);

        // Assert
        Assert.That(result, Is.SameAs(target));
    }

    [Test]
    public void Evaluate_WithPropertyPath_ShouldReturnPropertyValue()
    {
        // Arrange
        var root = new TestEngineObject();
        var target = new TestEngineObjectWithValue(42.0);
        root.AddChild(target);
        var objectId = target.Id;
        var expression = new ReferenceExpression<double>(objectId, "Value");
        var propertyLookup = new PropertyLookup(root);
        var context = new ExpressionContext(TimeSpan.Zero, Property.Create(0.0), propertyLookup);

        // Act
        var result = expression.Evaluate(context);

        // Assert
        Assert.That(result, Is.EqualTo(42.0));
    }

    [Test]
    public void Evaluate_WithInvalidPropertyPath_ShouldReturnDefault()
    {
        // Arrange
        var root = new TestEngineObject();
        var target = new TestEngineObject();
        root.AddChild(target);
        var objectId = target.Id;
        var expression = new ReferenceExpression<double>(objectId, "NonExistentProperty");
        var propertyLookup = new PropertyLookup(root);
        var context = new ExpressionContext(TimeSpan.Zero, Property.Create(0.0), propertyLookup);

        // Act
        var result = expression.Evaluate(context);

        // Assert
        Assert.That(result, Is.EqualTo(default(double)));
    }

    [Test]
    public void Evaluate_WithTypeMismatch_ShouldReturnDefault()
    {
        // Arrange
        var root = new TestEngineObject();
        var target = new TestEngineObject();
        root.AddChild(target);
        var objectId = target.Id;
        // Try to get TestEngineObject as string
        var expression = new ReferenceExpression<string>(objectId);
        var propertyLookup = new PropertyLookup(root);
        var context = new ExpressionContext(TimeSpan.Zero, Property.Create(0.0), propertyLookup);

        // Act
        var result = expression.Evaluate(context);

        // Assert
        Assert.That(result, Is.Null);
    }
}

public partial class TestEngineObjectWithValue : EngineObject
{
    public TestEngineObjectWithValue(double initialValue)
    {
        ScanProperties<TestEngineObjectWithValue>();
        Value.CurrentValue = initialValue;
    }

    public IProperty<double> Value { get; } = Property.Create(0.0);
}
