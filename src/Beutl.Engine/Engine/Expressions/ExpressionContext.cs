using Beutl.Graphics.Rendering;

namespace Beutl.Engine.Expressions;

public class ExpressionContext(TimeSpan time, IProperty currentProperty, PropertyLookup propertyLookup) : RenderContext(time)
{
    private readonly HashSet<IProperty> _evaluationStack = [];

    public IProperty CurrentProperty { get; set; } = currentProperty;

    public PropertyLookup PropertyLookup { get; init; } = propertyLookup;

    public bool TryGetPropertyValue<T>(string path, out T? value)
    {
        return PropertyLookup.TryGetPropertyValue(path, this, out value);
    }

    public bool TryGetPropertyValue<T>(Guid objectId, string propertyName, out T? value)
    {
        return PropertyLookup.TryGetPropertyValue(objectId, propertyName, this, out value);
    }

    public bool IsEvaluating(IProperty property)
    {
        return _evaluationStack.Contains(property);
    }

    public void BeginEvaluation(IProperty property)
    {
        _evaluationStack.Add(property);
    }

    public void EndEvaluation(IProperty property)
    {
        _evaluationStack.Remove(property);
    }
}
