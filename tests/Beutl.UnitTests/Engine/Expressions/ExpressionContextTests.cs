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
}
