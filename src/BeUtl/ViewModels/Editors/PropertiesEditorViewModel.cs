using BeUtl.Services;
using BeUtl.Services.Editors.Wrappers;

namespace BeUtl.ViewModels.Editors;

public sealed class PropertiesEditorViewModel : IDisposable
{
    public PropertiesEditorViewModel(ICoreObject obj, Predicate<CorePropertyMetadata>? predicate = null)
    {
        Target = obj;
        InitializeCoreObject(obj, predicate);
    }

    public ICoreObject Target { get; }

    public CoreList<BaseEditorViewModel> Properties { get; } = new();

    public void Dispose()
    {
        foreach (BaseEditorViewModel item in Properties.GetMarshal().Value)
        {
            item.Dispose();
        }
    }

    private void InitializeCoreObject(ICoreObject obj, Predicate<CorePropertyMetadata>? predicate = null)
    {
        Type objType = obj.GetType();
        Type wrapperType = typeof(CorePropertyWrapper<>);
        Type animatableWrapperType = typeof(AnimatableCorePropertyWrapper<>);

        IReadOnlyList<CoreProperty> props = PropertyRegistry.GetRegistered(objType);
        Properties.EnsureCapacity(props.Count);

        for (int i = 0; i < props.Count; i++)
        {
            CoreProperty item = props[i];
            CorePropertyMetadata metadata = item.GetMetadata<CorePropertyMetadata>(objType);
            if (predicate?.Invoke(metadata) ?? true)
            {
                Type wtype = (metadata.PropertyFlags.HasFlag(PropertyFlags.Animatable) ? animatableWrapperType : wrapperType);
                Type wrapperGType = wtype.MakeGenericType(item.PropertyType);
                var wrapper = (IWrappedProperty)Activator.CreateInstance(wrapperGType, item, obj)!;

                BaseEditorViewModel? itemViewModel = PropertyEditorService.CreateEditorViewModel(wrapper);

                if (itemViewModel != null)
                {
                    Properties.Add(itemViewModel);
                }
            }
        }
    }
}
