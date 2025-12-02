namespace Beutl.Engine.Expressions;

public class ExpressionContext
{
    public required TimeSpan Time { get; init; }

    public required IProperty CurrentProperty { get; init; }

    public required PropertyLookup PropertyLookup { get; init; }

    public bool TryGetPropertyValue<T>(string path, out T? value)
    {
        return PropertyLookup.TryGetPropertyValue(path, this, out value);
    }

    public bool TryGetPropertyValue<T>(Guid objectId, string propertyName, out T? value)
    {
        return PropertyLookup.TryGetPropertyValue(objectId, propertyName, this, out value);
    }
}
