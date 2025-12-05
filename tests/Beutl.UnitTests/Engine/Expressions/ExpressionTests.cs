using Beutl.Engine.Expressions;

namespace Beutl.UnitTests.Engine.Expressions;

[TestFixture]
public class ExpressionTests
{
    [Test]
    public void Constructor_WithValidExpression_ShouldNotThrow()
    {
        // Arrange & Act
        var expression = new Expression<double>("1 + 2");

        // Assert
        Assert.That(expression.ExpressionString, Is.EqualTo("1 + 2"));
    }

    [Test]
    public void Constructor_WithNullExpression_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new Expression<double>(null!));
    }

    [Test]
    public void ResultType_ShouldReturnCorrectType()
    {
        // Arrange
        var expression = new Expression<double>("1.0");

        // Assert
        Assert.That(expression.ResultType, Is.EqualTo(typeof(double)));
    }

    [Test]
    public void Validate_WithValidExpression_ShouldReturnTrue()
    {
        // Arrange
        var expression = new Expression<double>("1 + 2 * 3");

        // Act
        bool result = expression.Validate(out string? error);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(error, Is.Null);
    }

    [Test]
    public void Validate_WithInvalidExpression_ShouldReturnFalse()
    {
        // Arrange
        var expression = new Expression<double>("invalid syntax ++");

        // Act
        bool result = expression.Validate(out string? error);

        // Assert
        Assert.That(result, Is.False);
        Assert.That(error, Is.Not.Null);
    }

    [Test]
    public void Evaluate_WithSimpleArithmetic_ShouldReturnCorrectResult()
    {
        // Arrange
        var expression = new Expression<double>("1 + 2 * 3");
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);

        // Act
        double result = expression.Evaluate(context);

        // Assert
        Assert.That(result, Is.EqualTo(7.0));
    }

    [Test]
    public void Evaluate_WithMathFunctions_ShouldWork()
    {
        // Arrange
        var expression = new Expression<double>("Sin(0.0)");
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);

        // Act
        double result = expression.Evaluate(context);

        // Assert
        Assert.That(result, Is.EqualTo(0.0).Within(0.0001));
    }

    [Test]
    public void Evaluate_WithTimeProperty_ShouldWork()
    {
        // Arrange
        var expression = new Expression<double>("Time");
        var context = TestHelper.CreateExpressionContext(TimeSpan.FromSeconds(5));

        // Act
        double result = expression.Evaluate(context);

        // Assert
        Assert.That(result, Is.EqualTo(5.0));
    }

    [Test]
    public void Evaluate_WithInvalidExpression_ShouldThrowExpressionException()
    {
        // Arrange
        var expression = new Expression<double>("invalid syntax ++");
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);

        // Act & Assert
        Assert.Throws<ExpressionException>(() => expression.Evaluate(context));
    }

    [Test]
    public void Evaluate_WithIntReturnType_ShouldConvertToDouble()
    {
        // Arrange
        var expression = new Expression<double>("1 + 2");
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);

        // Act
        double result = expression.Evaluate(context);

        // Assert
        Assert.That(result, Is.EqualTo(3.0));
    }

    [Test]
    public void Evaluate_WithIntExpression_ShouldWork()
    {
        // Arrange
        var expression = new Expression<int>("1 + 2");
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);

        // Act
        int result = expression.Evaluate(context);

        // Assert
        Assert.That(result, Is.EqualTo(3));
    }

    [Test]
    public void Evaluate_WithBoolExpression_ShouldWork()
    {
        // Arrange
        var expression = new Expression<bool>("1 > 0");
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);

        // Act
        bool result = expression.Evaluate(context);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void Evaluate_WithStringExpression_ShouldWork()
    {
        // Arrange
        var expression = new Expression<string>("\"Hello\"");
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);

        // Act
        string result = expression.Evaluate(context);

        // Assert
        Assert.That(result, Is.EqualTo("Hello"));
    }

    [Test]
    public void ToString_ShouldReturnExpressionString()
    {
        // Arrange
        var expression = new Expression<double>("1 + 2");

        // Act
        string result = expression.ToString();

        // Assert
        Assert.That(result, Is.EqualTo("1 + 2"));
    }

    [Test]
    public void Create_ShouldCreateExpressionWithCorrectString()
    {
        // Act
        var expression = Expression.Create<double>("1 + 2");

        // Assert
        Assert.That(expression.ExpressionString, Is.EqualTo("1 + 2"));
    }

    [Test]
    public void TryParse_WithValidExpression_ShouldReturnTrue()
    {
        // Act
        bool result = Expression.TryParse<double>("1 + 2", out var expression, out string? error);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(expression, Is.Not.Null);
        Assert.That(error, Is.Null);
    }

    [Test]
    public void TryParse_WithInvalidExpression_ShouldReturnFalse()
    {
        // Act
        bool result = Expression.TryParse<double>("invalid ++", out var expression, out string? error);

        // Assert
        Assert.That(result, Is.False);
        Assert.That(expression, Is.Null);
        Assert.That(error, Is.Not.Null);
    }
}
