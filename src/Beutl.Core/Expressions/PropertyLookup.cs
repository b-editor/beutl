namespace Beutl;

public class PropertyLookup(ICoreObject root)
{
    private readonly ICoreObject _root = root ?? throw new ArgumentNullException(nameof(root));

    public ICoreObject? FindById(Guid id)
    {
        return _root.FindById(id);
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

        if (_root.FindById(id) is not CoreObject coreObject)
            return false;

        IProperty? property = coreObject.Properties.FirstOrDefault(p =>
            string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));

        if (property is IProperty<T> typedProperty)
        {
            value = typedProperty.GetValue(context);
            return true;
        }

        Type type = coreObject.GetType();
        CoreProperty? coreProperty = PropertyRegistry.GetRegistered(type)
            .FirstOrDefault(p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));

        if (coreProperty is CoreProperty<T> typedCoreProperty)
        {
            value = coreObject.GetValue(typedCoreProperty);
            return true;
        }

        return false;
    }
}
