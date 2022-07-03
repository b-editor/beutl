using BeUtl.Services;
using BeUtl.Services.Editors.Wrappers;

namespace BeUtl.ViewModels.Editors;

public sealed class PropertiesEditorViewModel : IDisposable
{
    public PropertiesEditorViewModel(ICoreObject obj)
    {
        Target = obj;
        Type objType = obj.GetType();
        Type wrapperType = typeof(CorePropertyWrapper<>);

        IReadOnlyList<CoreProperty> props = PropertyRegistry.GetRegistered(objType);
        if (Properties.Capacity < props.Count)
        {
            Properties.Capacity = props.Count;
        }

        for (int i = 0; i < props.Count; i++)
        {
            CoreProperty item = props[i];
            Type wrapperGType = wrapperType.MakeGenericType(item.PropertyType);
            var wrapper = (IWrappedProperty)Activator.CreateInstance(wrapperGType, item, obj)!;

            BaseEditorViewModel? itemViewModel = PropertyEditorService.CreateEditorViewModel(wrapper);

            if (itemViewModel != null)
            {
                Properties.Add(itemViewModel);
            }
        }
    }

    public ICoreObject Target { get; }

    public CoreList<BaseEditorViewModel> Properties { get; } = new();

    public void Dispose()
    {
        foreach (BaseEditorViewModel item in Properties.AsSpan())
        {
            item.Dispose();
        }
    }
}
