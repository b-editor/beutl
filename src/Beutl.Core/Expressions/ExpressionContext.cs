namespace Beutl;

public class ExpressionContext : EvaluationContext
{
    private readonly HashSet<IProperty> _evaluationStack;

    public ExpressionContext(MediaTime time, IProperty currentProperty, PropertyLookup propertyLookup)
        : this(time, currentProperty, propertyLookup, [])
    {
    }

    private ExpressionContext(MediaTime time, IProperty currentProperty, PropertyLookup propertyLookup,
        HashSet<IProperty> evaluationStack) : base(time)
    {
        CurrentProperty = currentProperty;
        PropertyLookup = propertyLookup;
        _evaluationStack = evaluationStack;
    }

    public IProperty CurrentProperty { get; set; }

    public PropertyLookup PropertyLookup { get; init; }

    public ExpressionContext WithTime(MediaTime time)
    {
        return new ExpressionContext(time, CurrentProperty, PropertyLookup, _evaluationStack)
        {
            PreviewTimeRange = PreviewTimeRange
        };
    }

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
