using Beutl.Animation;
using Beutl.Framework;
using Beutl.Services;
using Beutl.Services.Editors.Wrappers;

using DynamicData;

namespace Beutl.ViewModels.Editors;

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
        bool isAnimatable = obj is IAnimatable;

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
                var tmp = new IAbstractProperty[foundItems.Length];
                for (int i = 0; i < foundItems.Length; i++)
                {
                    CoreProperty item = foundItems[i];
                    CorePropertyMetadata metadata = item.GetMetadata<CorePropertyMetadata>(objType);
                    Type wtype = isAnimatable && metadata.PropertyFlags.HasFlag(PropertyFlags.Animatable) ? animatableWrapperType : wrapperType;
                    Type wrapperGType = wtype.MakeGenericType(item.PropertyType);
                    tmp[i] = (IAbstractProperty)Activator.CreateInstance(wrapperGType, item, obj)!;
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
