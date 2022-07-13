using Avalonia;

using BeUtl.Services;
using BeUtl.Services.Editors.Wrappers;
using BeUtl.Styling;

namespace BeUtl.ViewModels.Editors;

public sealed class PropertiesEditorViewModel : IDisposable
{
    public PropertiesEditorViewModel(ICoreObject obj, Predicate<CorePropertyMetadata>? predicate = null)
    {
        Target = obj;
        if (obj is Styleable styleable)
        {
            InitializeStyleable(styleable, predicate);
        }
        else
        {
            InitializeCoreObject(obj, predicate);
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

    private void InitializeCoreObject(ICoreObject obj, Predicate<CorePropertyMetadata>? predicate = null)
    {
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
            if (predicate?.Invoke(item.GetMetadata<CorePropertyMetadata>(objType)) ?? true)
            {
                Type wrapperGType = wrapperType.MakeGenericType(item.PropertyType);
                var wrapper = (IWrappedProperty)Activator.CreateInstance(wrapperGType, item, obj)!;

                BaseEditorViewModel? itemViewModel = PropertyEditorService.CreateEditorViewModel(wrapper);

                if (itemViewModel != null)
                {
                    Properties.Add(itemViewModel);
                }
            }
        }
    }

    private void InitializeStyleable(Styleable styleable, Predicate<CorePropertyMetadata>? predicate = null)
    {
        predicate ??= x => x.PropertyFlags.HasFlag(PropertyFlags.Styleable);
        Type objType = styleable.GetType();
        IReadOnlyList<CoreProperty> props = PropertyRegistry.GetRegistered(objType);

        var style = styleable.Styles.FirstOrDefault();
        if (style == null)
        {
            var style1 = new Style()
            {
                TargetType = objType
            };

            for (int i = 0; i < props.Count; i++)
            {
                CoreProperty item = props[i];
                CorePropertyMetadata metadata = item.GetMetadata<CorePropertyMetadata>(objType);
                Type setterType = typeof(Setter<>).MakeGenericType(item.PropertyType);
                if (predicate.Invoke(metadata)
                    && Activator.CreateInstance(setterType, item, styleable.GetValue(item)) is ISetter setter)
                {
                    style1.Setters.Add(setter);
                }
            }

            styleable.Styles.Add(style1);
            style = style1;
        }

        if (Properties.Capacity < style.Setters.Count)
        {
            Properties.Capacity = style.Setters.Count;
        }

        Type wrapperType = typeof(StylingSetterWrapper<>);
        for (int i = 0; i < style.Setters.Count; i++)
        {
            ISetter item = style.Setters[i];
            CoreProperty property = item.Property;

            if (predicate?.Invoke(property.GetMetadata<CorePropertyMetadata>(objType)) ?? true)
            {
                Type wrapperGType = wrapperType.MakeGenericType(property.PropertyType);
                var wrapper = (IWrappedProperty)Activator.CreateInstance(wrapperGType, item)!;

                BaseEditorViewModel? itemViewModel = PropertyEditorService.CreateEditorViewModel(wrapper);

                if (itemViewModel != null)
                {
                    Properties.Add(itemViewModel);
                }
            }
        }
    }
}
