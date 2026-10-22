using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Beutl.Api.Services;
using Beutl.PropertyAdapters;
using DynamicData;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

// Building blocks shared by the hosts that edit the properties of a settings object.
internal static class CoreObjectPropertyEditorBuilder
{
    // A settings object's properties as its page shows them: without Id, Name and those marked [Browsable(false)].
    public static List<IPropertyAdapter> CreateSettingsAdapters(CoreObject obj)
    {
        return
        [
            .. obj.Properties
                .Where(p => !obj.IsOwnField(p)
                            && p.GetAttributes()?.OfType<BrowsableAttribute>().FirstOrDefault()?.Browsable != false)
                .Select(p => CreateAdapter(p, obj)),
        ];
    }

    public static IPropertyAdapter CreateAdapter(IProperty property, CoreObject obj)
        => (IPropertyAdapter)Activator.CreateInstance(
            typeof(EnginePropertyAdapter<>).MakeGenericType(property.ValueType), property, obj)!;

    public static (IPropertyAdapter[]? Properties, PropertyEditorExtension? Extension) MatchProperty(
        IReadOnlyList<IPropertyAdapter> properties, ExtensionProvider extensionProvider)
    {
        ArgumentNullException.ThrowIfNull(extensionProvider);

        PropertyEditorExtension[] items = extensionProvider.GetExtensions<PropertyEditorExtension>();
        for (int i = items.Length - 1; i >= 0; i--)
        {
            PropertyEditorExtension item = items[i];
            IPropertyAdapter[] array = ExtensionGuard.Call(item, () => item.MatchProperty(properties).ToArray()) ?? [];
            if (array.Length > 0)
            {
                return (array, item);
            }
        }

        return default;
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
            (foundItems, extension) = MatchProperty(adapters, extensionProvider);
            if (foundItems != null && extension != null)
            {
                PropertyEditorExtension owner = extension;
                IPropertyAdapter[] items = foundItems;
                IPropertyEditorContext? context = ExtensionGuard.Call(owner, () =>
                {
                    IPropertyEditorContext? created;
                    return (forSettings
                        ? owner.TryCreateContextForSettings(items, out created)
                        : owner.TryCreateContext(items, out created)) ? created : null;
                });
                if (context != null)
                {
                    add(context);
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
