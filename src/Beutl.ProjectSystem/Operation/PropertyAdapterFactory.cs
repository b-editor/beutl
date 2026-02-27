using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Extensibility;

namespace Beutl.Operation;

public static class PropertyAdapterFactory
{
    public static List<IPropertyAdapter> CreateAdapters(EngineObject obj)
    {
        var adapters = new List<IPropertyAdapter>();
        foreach (var property in obj.GetDisplayProperties())
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

            var adapter = (IPropertyAdapter)Activator.CreateInstance(adapterType, property, obj)!;
            adapters.Add(adapter);
        }

        return adapters;
    }
}
