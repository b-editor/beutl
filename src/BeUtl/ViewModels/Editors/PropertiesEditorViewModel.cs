using BeUtl.Framework;
using BeUtl.Services;
using BeUtl.Services.Editors.Wrappers;

using DynamicData;

namespace BeUtl.ViewModels.Editors;

public sealed class PropertiesEditorViewModel : IDisposable
{
    public PropertiesEditorViewModel(ICoreObject obj, Predicate<CorePropertyMetadata>? predicate = null)
    {
        Target = obj;
        InitializeCoreObject(obj, predicate);
    }

    public ICoreObject Target { get; }

    public CoreList<IPropertyEditorContext> Properties { get; } = new();

    public void Dispose()
    {
        foreach (IPropertyEditorContext item in Properties.GetMarshal().Value)
        {
            item.Dispose();
        }
    }

    private void InitializeCoreObject(ICoreObject obj, Predicate<CorePropertyMetadata>? predicate = null)
    {
        Type objType = obj.GetType();
        Type wrapperType = typeof(CorePropertyClientImpl<>);
        Type animatableWrapperType = typeof(AnimatableCorePropertyClientImpl<>);

        List<CoreProperty> props = PropertyRegistry.GetRegistered(objType).ToList();
        Properties.EnsureCapacity(props.Count);
        CoreProperty[]? foundItems;
        PropertyEditorExtension? extension;
        props.RemoveAll(x => !(predicate?.Invoke(x.GetMetadata<CorePropertyMetadata>(objType)) ?? true));

        do
        {
            (foundItems, extension) = PropertyEditorService.MatchProperty(props);
            if (foundItems != null && extension != null)
            {
                int index = 0;
                var tmp = new IAbstractProperty[foundItems.Length];
                foreach (CoreProperty item in foundItems)
                {
                    CorePropertyMetadata metadata = item.GetMetadata<CorePropertyMetadata>(objType);
                    Type wtype = metadata.PropertyFlags.HasFlag(PropertyFlags.Animatable) ? animatableWrapperType : wrapperType;
                    Type wrapperGType = wtype.MakeGenericType(item.PropertyType);
                    tmp[index] = (IAbstractProperty)Activator.CreateInstance(wrapperGType, item, obj)!;

                    index++;
                }

                if (extension.TryCreateContext(tmp, out IPropertyEditorContext? context))
                {
                    Properties.Add(context);
                }

                props.RemoveMany(foundItems);
            }
        } while (foundItems != null && extension != null);
    }
}
