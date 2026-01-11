using Beutl.Engine;

namespace Beutl.UnitTests.Engine.Expressions;

[TestFixture]
public class ExpressionContextTests
{
    [Test]
    public void Constructor_ShouldSetProperties()
    {
        // Arrange
        var time = TimeSpan.FromSeconds(5);
        var context = TestHelper.CreateExpressionContext(time);

        // Assert
        Assert.That(context.Time, Is.EqualTo(time));
        Assert.That(context.CurrentProperty, Is.Not.Null);
        Assert.That(context.PropertyLookup, Is.Not.Null);
    }

    [Test]
    public void CurrentProperty_CanBeChanged()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var newProperty = Property.Create(0.0);

        // Act
        context.CurrentProperty = newProperty;

        // Assert
        Assert.That(context.CurrentProperty, Is.EqualTo(newProperty));
    }

    [Test]
    public void TryGetPropertyValue_WithInvalidPath_ShouldReturnFalse()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);

        // Act
        bool result = context.TryGetPropertyValue<double>("invalid-path", out var value);

        // Assert
        Assert.That(result, Is.False);
        Assert.That(value, Is.EqualTo(default(double)));
    }

    [Test]
    public void TryGetPropertyValue_WithGuidAndPropertyName_ShouldDelegateToPropertyLookup()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var guid = Guid.NewGuid();

        // Act
        bool result = context.TryGetPropertyValue<double>(guid, "PropertyName", out var value);

        // Assert
        Assert.That(result, Is.False); // PropertyLookup returns false for non-existent objects
        Assert.That(value, Is.EqualTo(default(double)));
    }

    [Test]
    public void IsEvaluating_WithoutBeginEvaluation_ShouldReturnFalse()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var property = Property.Create(0.0);

        // Act
        bool result = context.IsEvaluating(property);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsEvaluating_AfterBeginEvaluation_ShouldReturnTrue()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var property = Property.Create(0.0);

        // Act
        context.BeginEvaluation(property);
        bool result = context.IsEvaluating(property);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void IsEvaluating_AfterEndEvaluation_ShouldReturnFalse()
    {
        // Arrange
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);
        var property = Property.Create(0.0);
        context.BeginEvaluation(property);

        // Act
        context.EndEvaluation(property);
        bool result = context.IsEvaluating(property);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void EvaluationStack_ShouldTrackMultipleProperties()
    {
        // Arrange
        var property1 = Property.Create(0.0);
        var property2 = Property.Create(0.0);
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);

        // Act
        context.BeginEvaluation(property1);
        context.BeginEvaluation(property2);

        // Assert
        Assert.That(context.IsEvaluating(property1), Is.True);
        Assert.That(context.IsEvaluating(property2), Is.True);

        // Clean up
        context.EndEvaluation(property2);
        Assert.That(context.IsEvaluating(property1), Is.True);
        Assert.That(context.IsEvaluating(property2), Is.False);

        context.EndEvaluation(property1);
        Assert.That(context.IsEvaluating(property1), Is.False);
    }

    [Test]
    public void Time_ShouldBeInheritedFromRenderContext()
    {
        // Arrange
        var time = TimeSpan.FromMilliseconds(1234);

        // Act
        var context = TestHelper.CreateExpressionContext(time);

        // Assert
        Assert.That(context.Time, Is.EqualTo(time));
    }
}
