using Beutl.Engine;
using Beutl.Engine.Expressions;

namespace Beutl.UnitTests.Engine.Expressions;

public static class TestHelper
{
    public static ExpressionContext CreateExpressionContext(TimeSpan time)
    {
        var root = new TestCoreObject();
        var propertyLookup = new PropertyLookup(root);
        var property = Property.Create(0.0);
        return new ExpressionContext(time, property, propertyLookup);
    }

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

    public IProperty<double> Value { get; } = Property.Create(0.0);

    public void AddChild(IHierarchical child)
    {
        HierarchicalChildren.Add(child);
    }
}
