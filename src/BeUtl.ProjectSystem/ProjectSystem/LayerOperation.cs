using System.Text.Json.Nodes;

using BeUtl.Collections;
using BeUtl.Rendering;

namespace BeUtl.ProjectSystem;

public interface IScopedRenderable : IList<IRenderable>
{
    void Append(IRenderable item)
    {
        item.IsVisible = true;
        if (!Contains(item))
        {
            Add(item);
        }
    }

    void Invalidate(IRenderable item)
    {
        item.IsVisible = false;
        if (!Contains(item))
        {
            Add(item);
        }
    }
}

public abstract class LayerOperation : Element, ILogicalElement
{
    public static readonly CoreProperty<bool> IsEnabledProperty;
    public static readonly CoreProperty<RenderOperationViewState> ViewStateProperty;
    private readonly LogicalList<ISetter> _setters;
    private bool _isEnabled = true;

    static LayerOperation()
    {
        IsEnabledProperty = ConfigureProperty<bool, LayerOperation>(nameof(IsEnabled))
            .Accessor(o => o.IsEnabled, (o, v) => o.IsEnabled = v)
            .DefaultValue(true)
            .Observability(PropertyObservability.Changed)
            .JsonName("isEnabled")
            .Register();

        ViewStateProperty = ConfigureProperty<RenderOperationViewState, LayerOperation>(nameof(ViewState))
            .Observability(PropertyObservability.Changed)
            .Register();
    }

    public LayerOperation()
    {
        _setters = new LogicalList<ISetter>(this);
        ViewState = new RenderOperationViewState();

        Type ownerType = GetType();
        foreach ((CoreProperty property, CorePropertyMetadata metadata) in PropertyRegistry.GetRegistered(ownerType)
            .Select(x => (property: x, metadata: x.GetMetadata(ownerType)))
            .Where(x => x.metadata.GetValueOrDefault<bool>(PropertyMetaTableKeys.Editor) == true))
        {
            Type? type;

            if (metadata.GetValueOrDefault(PropertyMetaTableKeys.IsAnimatable, false))
            {
                type = typeof(AnimatableSetter<>).MakeGenericType(property.PropertyType);
            }
            else
            {
                type = typeof(Setter<>).MakeGenericType(property.PropertyType);
            }

            if (Activator.CreateInstance(type, property) is ISetter setter)
            {
                setter.Parent = this;
                _setters.Add(setter);

                if (!metadata.GetValueOrDefault(PropertyMetaTableKeys.SuppressAutoRender, false))
                {
                    setter.GetObservable().Subscribe(_ => ForceRender());
                }
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

    public IObservableList<ISetter> Setters => _setters;

    IEnumerable<ILogicalElement> ILogicalElement.LogicalChildren => _setters;

    public virtual void ApplySetters(in OperationRenderArgs args)
    {
        int length = Setters.Count;
        for (int i = 0; i < length; i++)
        {
            ISetter item = Setters[i];

            if (item is IAnimatableSetter anmSetter)
            {
                anmSetter.SetProperty(args.CurrentTime);
            }
            else
            {
                item.SetProperty();
            }
        }
    }

    public virtual void BeginningRender(IScopedRenderable scope)
    {
    }

    public virtual void EndingRender(IScopedRenderable scope)
    {
    }

    public virtual void Render(in OperationRenderArgs args)
    {

    }

    public override void FromJson(JsonNode json)
    {
        var removed = new List<KeyValuePair<string, JsonNode>>();
        if (json is JsonObject jsonObject)
        {
            Type ownerType = GetType();
            for (int i = 0; i < _setters.Count; i++)
            {
                ISetter setter = _setters[i];
                string? jsonName = setter.Property.GetMetadata(ownerType).GetValueOrDefault<string>(PropertyMetaTableKeys.JsonName);
                if (jsonName != null && jsonObject.TryGetPropertyValue(jsonName, out JsonNode? node))
                {
                    setter.FromJson(node!);

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
            for (int i = 0; i < _setters.Count; i++)
            {
                ISetter setter = _setters[i];
                string? jsonName = setter.Property.GetMetadata(ownerType).GetValueOrDefault<string>(PropertyMetaTableKeys.JsonName);
                if (jsonName != null)
                {
                    jsonObject[jsonName] = setter.ToJson();
                }
            }
        }

        return node;
    }

    protected ISetter? FindSetter(CoreProperty property)
    {
        foreach (ISetter? item in _setters)
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

    public override void Render(in OperationRenderArgs args)
    {
    }
}
