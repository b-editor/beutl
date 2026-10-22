using Beutl.Engine.Expressions;
using Beutl.NodeGraph;

namespace Beutl.UnitTests.Engine.Expressions;

// Every expression can keep the one it replaced, which the property gets back once it is removed.
[TestFixture]
public class ReplacedExpressionTests
{
    [Test]
    public void StringExpression_OverAnother_KeepsItAndEvaluatesTheSame()
    {
        var below = Expression.Create<float>("1");
        var expression = Expression.Create<float>("Time + 2");

        var over = (StringExpression<float>)((IReplacingExpression<float>)expression).WithReplaced(below);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(over.Replaced, Is.SameAs(below));
            Assert.That(over.ExpressionString, Is.EqualTo(expression.ExpressionString));
            Assert.That(over.Evaluate(TestHelper.CreateExpressionContext(MediaTime.FromSeconds(1))), Is.EqualTo(3f));
            Assert.That(((IExpression)over).Replaced, Is.SameAs(below));
        }
    }

    [Test]
    public void ReferenceExpression_OverAnother_KeepsItWhenRebound()
    {
        var below = Expression.Create<double>("1");
        var expression = Expression.CreateReference<double>(Guid.NewGuid(), "Value");

        var over = (ReferenceExpression<double>)((IReplacingExpression<double>)expression).WithReplaced(below);
        var rebound = (ReferenceExpression<double>)over.Rebind(Guid.NewGuid())!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(over.Replaced, Is.SameAs(below));
            Assert.That(over.PropertyPath, Is.EqualTo("Value"));
            Assert.That(rebound.Replaced, Is.SameAs(below));
        }
    }

    [Test]
    public void NodePortExpression_OverAnother_KeepsItsValue()
    {
        var below = Expression.Create<float>("1");
        var expression = new NodePortExpression<float> { Value = 4 };

        var over = (NodePortExpression<float>)((IReplacingExpression<float>)expression).WithReplaced(below);

        Assert.That((over.Value, over.Replaced), Is.EqualTo((4f, below)));
    }

    [Test]
    public void AnExpressionThatKeepsNothing_HasNoReplaced()
    {
        IExpression<float> expression = new ConstantExpression(1);

        Assert.That((expression.Replaced, ((IExpression)expression).Replaced), Is.EqualTo((null as IExpression<float>, null as IExpression)));
    }

    private sealed class ConstantExpression(float value) : IExpression<float>
    {
        public string ExpressionString => "constant";

        public bool Validate(out string? error)
        {
            error = null;
            return true;
        }

        public float Evaluate(ExpressionContext context) => value;
    }
}
