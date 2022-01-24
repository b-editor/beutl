using System.Text.Json.Nodes;

using BeUtl.Collections;

namespace BeUtl.ProjectSystem;

public abstract class LayerOperation : Element, ILogicalElement
{
    public static readonly CoreProperty<bool> IsEnabledProperty;
    public static readonly CoreProperty<RenderOperationViewState> ViewStateProperty;
    private readonly CoreList<IPropertyInstance> _properties;
    private bool _isEnabled = true;

    static LayerOperation()
    {
        IsEnabledProperty = ConfigureProperty<bool, LayerOperation>(nameof(IsEnabled))
            .Accessor(o => o.IsEnabled, (o, v) => o.IsEnabled = v)
            .DefaultValue(true)
            .Observability(PropertyObservability.Changed)
            .SerializeName("isEnabled")
            .Register();

        ViewStateProperty = ConfigureProperty<RenderOperationViewState, LayerOperation>(nameof(ViewState))
            .Observability(PropertyObservability.Changed)
            .Register();
    }

    public LayerOperation()
    {
        _properties = new CoreList<IPropertyInstance>();
        ViewState = new RenderOperationViewState();

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

    public RenderOperationViewState ViewState
    {
        get => GetValue(ViewStateProperty);
        set => SetValue(ViewStateProperty, value);
    }

    public IObservableList<IPropertyInstance> Properties => _properties;

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

    protected IPropertyInstance? FindSetter(CoreProperty property)
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
            renderer.Invalidate();
        }
    }
}

public sealed class EmptyOperation : LayerOperation
{
    public EmptyOperation()
    {
    }
}
