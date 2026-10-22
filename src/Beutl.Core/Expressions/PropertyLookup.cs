namespace Beutl;

public class PropertyLookup(ICoreObject root)
{
    private readonly ICoreObject _root = root ?? throw new ArgumentNullException(nameof(root));
    private readonly IHierarchical? _anchor;

    // The lookup of a property's expression, which resolves ids within anchor's scene first.
    internal PropertyLookup(ICoreObject root, IHierarchical? anchor) : this(root)
    {
        _anchor = anchor;
    }

    public ICoreObject? FindById(Guid id)
    {
        return _anchor is null ? _root.FindById(id) : ReferenceScope.Find(_anchor, id, _root);
    }

    public bool TryGetPropertyValue<T>(string path, ExpressionContext context, out T? value)
    {
        value = default;

        int dotIndex = path.IndexOf('.');
        if (dotIndex < 0)
            return false;

        string objectIdentifier = path[..dotIndex];
        string propertyName = path[(dotIndex + 1)..];

        if (!Guid.TryParse(objectIdentifier, out Guid objectId))
            return false;

        return TryGetPropertyValue(objectId, propertyName, context, out value);
    }

    public bool TryGetPropertyValue<T>(Guid id, string propertyName, ExpressionContext context, out T? value)
    {
        value = default;

        if (FindById(id) is not CoreObject coreObject)
            return false;

        IProperty? property = coreObject.Properties.FirstOrDefault(p =>
            string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));

        if (property is IProperty<T> typedProperty)
        {
            value = typedProperty is IExpressionProperty<T> expressionProperty
                ? expressionProperty.Evaluate(context)
                : typedProperty.Value;
            return true;
        }

        // Expressions saved before #2885 read times as TimeSpan.
        if (typeof(T) == typeof(TimeSpan) && property is IProperty<MediaTime> timeProperty)
        {
            MediaTime time = timeProperty is IExpressionProperty<MediaTime> expressionTime
                ? expressionTime.Evaluate(context)
                : timeProperty.Value;
            value = (T)(object)time.ToTimeSpan();
            return true;
        }

        return false;
    }
}
