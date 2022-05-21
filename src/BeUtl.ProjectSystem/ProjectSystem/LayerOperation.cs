using System.Text.Json.Nodes;

using BeUtl.Collections;

namespace BeUtl.ProjectSystem;

public abstract class LayerOperation : Element, ILogicalElement
{
    public static readonly CoreProperty<bool> IsEnabledProperty;
    private readonly LogicalList<IPropertyInstance> _properties;
    private bool _isEnabled = true;

    static LayerOperation()
    {
        IsEnabledProperty = ConfigureProperty<bool, LayerOperation>(nameof(IsEnabled))
            .Accessor(o => o.IsEnabled, (o, v) => o.IsEnabled = v)
            .DefaultValue(true)
            .Observability(PropertyObservability.Changed)
            .SerializeName("isEnabled")
            .Register();
    }

    public LayerOperation()
    {
        _properties = new LogicalList<IPropertyInstance>(this);

        Type ownerType = GetType();
        foreach ((CoreProperty property, CorePropertyMetadata metadata) in PropertyRegistry.GetRegistered(ownerType)
            .Select(x => (property: x, metadata: x.GetMetadata<CorePropertyMetadata>(ownerType)))
            .Where(x => x.metadata.PropertyFlags.HasFlag(PropertyFlags.Designable)))
        {
            IOperationPropertyMetadata opMetadata
                = property.GetMetadata<IOperationPropertyMetadata>(ownerType);
            Type? type;

            if (opMetadata.IsAnimatable == true)
            {
                type = typeof(AnimatablePropertyInstance<>).MakeGenericType(property.PropertyType);
            }
            else
            {
                type = typeof(PropertyInstance<>).MakeGenericType(property.PropertyType);
            }

            if (Activator.CreateInstance(type, property) is IPropertyInstance setter)
            {
                _properties.Add(setter);

                setter.GetObservable().Subscribe(_ => ForceRender());
            }
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetAndRaise(IsEnabledProperty, ref _isEnabled, value))
            {
                ForceRender();
            }
        }
    }

    public ICoreList<IPropertyInstance> Properties => _properties;

    public void Render(ref OperationRenderArgs args)
    {
        foreach (IPropertyInstance? item in _properties.AsSpan())
        {
            if (item is IAnimatablePropertyInstance anmProp)
            {
                anmProp.SetProperty(args.Renderer.Clock.CurrentTime);
            }
            else
            {
                item.SetProperty();
            }
        }

        RenderCore(ref args);
    }

    protected virtual void RenderCore(ref OperationRenderArgs args)
    {
    }

    public override void FromJson(JsonNode json)
    {
        var removed = new List<KeyValuePair<string, JsonNode>>();
        if (json is JsonObject jsonObject)
        {
            Type ownerType = GetType();
            for (int i = 0; i < _properties.Count; i++)
            {
                IPropertyInstance prop = _properties[i];
                string? jsonName = prop.Property.GetMetadata<CorePropertyMetadata>(ownerType).SerializeName;
                if (jsonName != null && jsonObject.TryGetPropertyValue(jsonName, out JsonNode? node))
                {
                    prop.FromJson(node!);

                    removed.Add(new(jsonName, node!));
                    jsonObject.Remove(jsonName);
                }
            }
        }

        base.FromJson(json);

        for (int i = 0; i < removed.Count; i++)
        {
            KeyValuePair<string, JsonNode> item = removed[i];
            json[item.Key] = item.Value;
        }
    }

    public override JsonNode ToJson()
    {
        JsonNode node = base.ToJson();

        if (node is JsonObject jsonObject)
        {
            Type ownerType = GetType();
            for (int i = 0; i < _properties.Count; i++)
            {
                IPropertyInstance prop = _properties[i];
                string? jsonName = prop.Property.GetMetadata<CorePropertyMetadata>(ownerType).SerializeName;
                if (jsonName != null)
                {
                    jsonObject[jsonName] = prop.ToJson();
                }
            }
        }

        return node;
    }

    protected IPropertyInstance? FindPropertyInstance(CoreProperty property)
    {
        foreach (IPropertyInstance? item in _properties)
        {
            if (item.Property == property)
            {
                return item;
            }
        }

        return null;
    }

    [Obsolete("Use 'FindPropertyInstance' method.")]
    protected IPropertyInstance? FindSetter(CoreProperty property)
    {
        return FindPropertyInstance(property);
    }

    protected void ForceRender()
    {
        Layer? layer = this.FindLogicalParent<Layer>();

        Scene? scene = this.FindLogicalParent<Scene>();
        if (scene != null &&
            layer != null &&
            layer.IsEnabled &&
            layer.Start <= scene.CurrentFrame &&
            scene.CurrentFrame < layer.Start + layer.Length &&
            scene?.Renderer is SceneRenderer renderer)
        {
            renderer.Invalidate(scene.CurrentFrame);
        }
    }
}

public sealed class EmptyOperation : LayerOperation
{
    public EmptyOperation()
    {
    }
}
