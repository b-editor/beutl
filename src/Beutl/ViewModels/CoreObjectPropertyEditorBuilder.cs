using System.ComponentModel.DataAnnotations;
using Beutl.PropertyAdapters;
using Beutl.ViewModels.Editors;
using DynamicData;

namespace Beutl.ViewModels;

// Building blocks shared by the hosts that edit the CoreProperty values of a settings object.
internal static class CoreObjectPropertyEditorBuilder
{
    public static List<IPropertyAdapter> CreateAdapters(
        ICoreObject obj, Func<CoreProperty, CorePropertyMetadata, bool>? predicate)
    {
        Type objType = obj.GetType();
        Type adapterType = typeof(CorePropertyAdapter<>);

        List<CoreProperty> cprops = [.. PropertyRegistry.GetRegistered(objType)];
        cprops.RemoveAll(x => !(predicate?.Invoke(x, x.GetMetadata<CorePropertyMetadata>(objType)) ?? true));
        return cprops.ConvertAll(x =>
        {
            CorePropertyMetadata metadata = x.GetMetadata<CorePropertyMetadata>(objType);
            Type adapterGType = adapterType.MakeGenericType(x.PropertyType);
            return (IPropertyAdapter)Activator.CreateInstance(adapterGType, x, obj)!;
        });
    }

    // Replaces the members of each named display group with one group context at the first member's place.
    public static void GroupByDisplayGroupName(List<IPropertyEditorContext?> items)
    {
        foreach ((string? Key, IPropertyEditorContext?[] Value) group in items
                     .GroupBy(x => GetDisplayAttribute(x)?.GetGroupName())
                     .Select(x => (x.Key, x.ToArray()))
                     .ToArray())
        {
            if (group.Key != null)
            {
                IPropertyEditorContext?[] array = group.Value;
                if (array.Length >= 1)
                {
                    int index = items.IndexOf(array[0]);
                    items.RemoveMany(array);
                    items.Insert(index, new PropertyEditorGroupContext(array, group.Key, index == 0));
                }
            }
        }
    }

    public static DisplayAttribute? GetDisplayAttribute(IPropertyEditorContext? context)
    {
        if (context is BaseEditorViewModel { PropertyAdapter: { } adapter })
        {
            return adapter.GetAttributes().FirstOrDefault(i => i is DisplayAttribute) as DisplayAttribute;
        }
        else
        {
            return null;
        }
    }
}
