using Beutl.Engine.Expressions;

namespace Beutl.UnitTests.Engine.Expressions;

[TestFixture]
public class ExpressionTests
{
    [TestCase("\"abc\"")]
    [TestCase("Time > 1")]
    [TestCase("return \"abc\";")]
    [TestCase("if (Time > 1) return true; return 1.0;")]
    [TestCase("new Point(1, 2)")]
    [TestCase("'a'")]
    public void TryParse_WithIncompatibleResult_RejectsExpression(string source)
    {
        bool result = Expression.TryParse<float>(source, out var expression, out string? error);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(expression, Is.Null);
            Assert.That(error, Does.Contain("float"));
        });
    }

    [TestCase("1.25", 1.25f)]
    [TestCase("1.25m", 1.25f)]
    [TestCase("(int?)2", 2f)]
    [TestCase("null", 0f)]
    [TestCase("Console.WriteLine()", 0f)]
    [TestCase("return 2.5;", 2.5f)]
    [TestCase("string Text() { return \"ignored\"; } 2.5", 2.5f)]
    [TestCase("Func<string> text = () => { return \"ignored\"; }; 2.5", 2.5f)]
    [TestCase("object result = 2.5; result", 2.5f)]
    public void TryParse_WithSupportedConversion_PreservesEvaluation(string source, float expected)
    {
        Assert.That(Expression.TryParse<float>(source, out var expression, out string? error), Is.True, error);
        Assert.That(expression!.Evaluate(TestHelper.CreateExpressionContext(TimeSpan.Zero)), Is.EqualTo(expected));
    }

    [TestCase("0", false)]
    [TestCase("-0.5", true)]
    public void TryParse_NumericToBool_PreservesConversion(string source, bool expected)
    {
        Assert.That(Expression.TryParse<bool>(source, out var expression, out string? error), Is.True, error);
        Assert.That(expression!.Evaluate(TestHelper.CreateExpressionContext(TimeSpan.Zero)), Is.EqualTo(expected));
    }

    [Test]
    public void TryParse_WithReferenceAndGenericResults_AcceptsAssignableValues()
    {
        Assert.That(Expression.TryParse<IEnumerable<int>>("new[] { 1, 2 }", out var expression, out string? error), Is.True, error);
        Assert.That(expression!.Evaluate(TestHelper.CreateExpressionContext(TimeSpan.Zero)), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(Expression.TryParse<Beutl.Media.Brush>("new SolidColorBrush()", out _, out error), Is.True, error);
        Assert.That(Expression.TryParse<int?>("1", out _, out error), Is.True, error);
    }

    [Test]
    public void Validate_DoesNotExecuteTheScript()
    {
        Assert.That(Expression.TryParse<float>("throw new InvalidOperationException();", out _, out string? error), Is.True, error);
    }

    [Test]
    public void Constructor_WithValidExpression_ShouldNotThrow()
    {
        // Arrange & Act
        var expression = new StringExpression<double>("1 + 2");

        // Assert
        Assert.That(expression.ExpressionString, Is.EqualTo("1 + 2"));
    }

    [Test]
    public void Constructor_WithNullExpression_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new StringExpression<double>(null!));
    }

    [Test]
    public void ResultType_ShouldReturnCorrectType()
    {
        // Arrange
        IExpression expression = new StringExpression<double>("1.0");

        // Assert
        Assert.That(expression.ResultType, Is.EqualTo(typeof(double)));
    }

    [Test]
    public void Validate_WithValidExpression_ShouldReturnTrue()
    {
        // Arrange
        var expression = new StringExpression<double>("1 + 2 * 3");

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
        var expression = new StringExpression<double>("invalid syntax ++");

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
        var expression = new StringExpression<double>("1 + 2 * 3");
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
        var expression = new StringExpression<double>("Sin(0.0)");
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
        var expression = new StringExpression<double>("Time");
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
        var expression = new StringExpression<double>("invalid syntax ++");
        var context = TestHelper.CreateExpressionContext(TimeSpan.Zero);

        // Act & Assert
        Assert.Throws<ExpressionException>(() => expression.Evaluate(context));
    }

    [Test]
    public void Evaluate_WithIntReturnType_ShouldConvertToDouble()
    {
        // Arrange
        var expression = new StringExpression<double>("1 + 2");
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
        var expression = new StringExpression<int>("1 + 2");
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
        var expression = new StringExpression<bool>("1 > 0");
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
        var expression = new StringExpression<string>("\"Hello\"");
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
        var expression = new StringExpression<double>("1 + 2");

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
