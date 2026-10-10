using Beutl.Engine;
using Beutl.Extensibility;

namespace Beutl.PropertyAdapters;

public static class PropertyAdapterFactory
{
    public static List<IPropertyAdapter> CreateAdapters(EngineObject obj)
    {
        var adapters = new List<IPropertyAdapter>();
        foreach (var property in obj.GetDisplayProperties())
        {
            adapters.Add(CreateAdapter(property, obj));
        }

        return adapters;
    }

    public static IPropertyAdapter CreateAdapter(IProperty property, EngineObject obj)
    {
        Type adapterType;
        var propertyType = property.GetType();
        if (propertyType.IsGenericType)
        {
            var genericTypeDef = propertyType.GetGenericTypeDefinition();
            if (genericTypeDef == typeof(AnimatableProperty<>))
            {
                adapterType = typeof(AnimatablePropertyAdapter<>).MakeGenericType(property.ValueType);
            }
            else if (genericTypeDef == typeof(SimpleProperty<>))
            {
                adapterType = typeof(SimplePropertyAdapter<>).MakeGenericType(property.ValueType);
            }
            else
            {
                adapterType = typeof(EnginePropertyAdapter<>).MakeGenericType(property.ValueType);
            }
        }
        else
        {
            adapterType = typeof(EnginePropertyAdapter<>).MakeGenericType(property.ValueType);
        }

        return (IPropertyAdapter)Activator.CreateInstance(adapterType, property, obj)!;
    }
}
