using System.ComponentModel.DataAnnotations;
using Beutl.Api.Services;
using Beutl.PropertyAdapters;
using Beutl.Services;
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
            Type adapterGType = adapterType.MakeGenericType(x.PropertyType);
            return (IPropertyAdapter)Activator.CreateInstance(adapterGType, x, obj)!;
        });
    }

    // Hands add the editor an extension makes for each group of adapters it takes, in the order the
    // extensions match them, and removes the adapters taken from adapters.
    public static void CreateContexts<TList>(
        TList adapters,
        ExtensionProvider extensionProvider,
        Action<IPropertyEditorContext> add,
        bool forSettings = false)
        where TList : IList<IPropertyAdapter>, IReadOnlyList<IPropertyAdapter>
    {
        IPropertyAdapter[]? foundItems;
        PropertyEditorExtension? extension;

        do
        {
            (foundItems, extension) = PropertyEditorService.MatchProperty(adapters, extensionProvider);
            if (foundItems != null && extension != null)
            {
                IPropertyEditorContext? context;
                bool created = forSettings
                    ? extension.TryCreateContextForSettings(foundItems, out context)
                    : extension.TryCreateContext(foundItems, out context);
                if (created)
                {
                    add(context!);
                }

                adapters.RemoveMany(foundItems);
            }
        } while (foundItems != null && extension != null);
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
                int index = items.IndexOf(array[0]);
                items.RemoveMany(array);
                items.Insert(index, new PropertyEditorGroupContext(array, group.Key, index == 0));
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
