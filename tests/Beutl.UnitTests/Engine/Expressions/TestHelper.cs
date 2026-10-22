using Beutl.Engine;
using Beutl.Engine.Expressions;

namespace Beutl.UnitTests.Engine.Expressions;

public static class TestHelper
{
    public static ExpressionContext CreateExpressionContext(TimeSpan time)
        => CreateExpressionContext(time, Property.CreateExpression(0.0));

    public static ExpressionContext CreateExpressionContext(TimeSpan time, IProperty property)
    {
        var root = new TestCoreObject();
        var propertyLookup = new PropertyLookup(root);
        return new ExpressionContext(time, property, propertyLookup);
    }
}

public class TestCoreObject : CoreObject
{
}

public partial class TestEngineObject : EngineObject
{
    public TestEngineObject()
    {
        ScanProperties<TestEngineObject>();
    }

    public IExpressionProperty<double> Value { get; } = Property.CreateExpression(0.0);

    public void AddChild(IHierarchical child)
    {
        HierarchicalChildren.Add(child);
    }
}
