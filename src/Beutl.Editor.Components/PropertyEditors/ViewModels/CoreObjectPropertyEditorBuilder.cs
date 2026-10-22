using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Beutl.Api.Services;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.PropertyEditors;
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

    // Hands add the editor's context for each group of adapters an editor takes, and removes the adapters taken.
    public static void CreateContexts(
        IList<IPropertyAdapter> adapters,
        ExtensionProvider extensionProvider,
        Action<IPropertyEditorContext> add,
        bool forSettings = false)
    {
        ArgumentNullException.ThrowIfNull(extensionProvider);

        PropertyEditorResolver.CreateContexts(
            adapters,
            extensionProvider.GetExtensions<PropertyEditorExtension>(),
            forSettings ? PropertyEditorSurface.Settings : PropertyEditorSurface.Panel,
            add);
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
        if (context is PropertyEditorViewModel { PropertyAdapter: { } adapter })
        {
            return adapter.GetAttributes().FirstOrDefault(i => i is DisplayAttribute) as DisplayAttribute;
        }
        else
        {
            return null;
        }
    }
}
