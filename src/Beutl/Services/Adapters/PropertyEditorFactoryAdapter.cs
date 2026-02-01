using Beutl.Editor.Services;
using Beutl.ViewModels.Editors;
using DynamicData;

namespace Beutl.Services.Adapters;

internal sealed class PropertyEditorFactoryAdapter : IPropertyEditorFactory
{
    public static readonly PropertyEditorFactoryAdapter Instance = new();

    public IPropertyEditorContext? CreateEditor(IPropertyAdapter property)
    {
        var (props, ext) = PropertyEditorService.MatchProperty([property]);
        if (props != null && ext != null && ext.TryCreateContext(props, out var ctx))
            return ctx;
        return null;
    }

    public (IPropertyAdapter[]? Properties, PropertyEditorExtension? Extension) MatchProperty(IReadOnlyList<IPropertyAdapter> properties)
    {
        return PropertyEditorService.MatchProperty(properties);
    }

    public IReadOnlyList<IPropertyEditorContext?> CreatePropertyEditorContexts(
        IReadOnlyList<IPropertyAdapter> properties,
        IPropertyEditorContextVisitor? visitor = null)
    {
        List<IPropertyAdapter> props = [.. properties];
        var tempItems = new List<IPropertyEditorContext?>(props.Count);
        IPropertyAdapter[]? foundItems;
        PropertyEditorExtension? extension;

        do
        {
            (foundItems, extension) = PropertyEditorService.MatchProperty(props);
            if (foundItems != null && extension != null)
            {
                if (extension.TryCreateContext(foundItems, out IPropertyEditorContext? context))
                {
                    tempItems.Add(context);
                    if (visitor != null)
                        context.Accept(visitor);
                }

                props.RemoveMany(foundItems);
            }
        } while (foundItems != null && extension != null);

        foreach ((string? Key, IPropertyEditorContext?[] Value) group in tempItems.GroupBy(x =>
                     {
                         if (x is BaseEditorViewModel { PropertyAdapter: { } adapter })
                         {
                             return (adapter.GetAttributes().FirstOrDefault(i => i is System.ComponentModel.DataAnnotations.DisplayAttribute) as System.ComponentModel.DataAnnotations.DisplayAttribute)
                                 ?.GetGroupName();
                         }
                         else
                         {
                             return null;
                         }
                     })
                     .Select(x => (x.Key, x.ToArray()))
                     .ToArray())
        {
            if (group.Key != null)
            {
                IPropertyEditorContext?[] array = group.Value;
                if (array.Length >= 1)
                {
                    int index = tempItems.IndexOf(array[0]);
                    tempItems.RemoveMany(array);
                    tempItems.Insert(index, new PropertyEditorGroupContext(array, group.Key, index == 0));
                }
            }
        }

        return tempItems;
    }
}
