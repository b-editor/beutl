using System.Text.Json.Nodes;

namespace BEditorNext.ProjectItems;

public abstract class RenderTask : Element
{
    public static readonly PropertyDefine<bool> IsEnabledProperty;
    private readonly ObservableList<ISetter> _setters = new();
    private bool _isEnabled;

    static RenderTask()
    {
        IsEnabledProperty = RegisterProperty<bool, RenderTask>(nameof(IsEnabled), (owner, obj) => owner.IsEnabled = obj, owner => owner.IsEnabled)
            .DefaultValue(true)
            .NotifyPropertyChanged(true)
            .JsonName("isEnabled");
    }

    public RenderTask()
    {
        foreach (PropertyDefine item in PropertyRegistry.GetRegistered(GetType())
            .Where(x => x.GetValueOrDefault<bool>(PropertyMetaTableKeys.Editor) == true))
        {
            if (item.GetValueOrDefault<bool>(PropertyMetaTableKeys.AnimationIsEnabled) == true)
            {
                Type type = typeof(AnimatableSetter<>).MakeGenericType(item.PropertyType);
                if (Activator.CreateInstance(type, item) is ISetter setter)
                {
                    _setters.Add(setter);
                }
            }
            else
            {
                Type type = typeof(Setter<>).MakeGenericType(item.PropertyType);
                if (Activator.CreateInstance(type, item) is ISetter setter)
                {
                    _setters.Add(setter);
                }
            }
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetAndRaise(IsEnabledProperty, ref _isEnabled, value);
    }

    public IObservableList<ISetter> Setters => _setters;

    public abstract void Execute(in RenderTaskExecuteArgs args);

    public override void FromJson(JsonNode json)
    {
        var removed = new List<KeyValuePair<string, JsonNode>>();
        if (json is JsonObject jsonObject)
        {
            for (int i = 0; i < _setters.Count; i++)
            {
                ISetter setter = _setters[i];
                string? jsonName = setter.Property.GetJsonName();
                if (jsonName != null && jsonObject.TryGetPropertyValue(jsonName!, out JsonNode? node))
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
            for (int i = 0; i < _setters.Count; i++)
            {
                ISetter setter = _setters[i];
                string? jsonName = setter.Property.GetJsonName();
                if (jsonName != null)
                {
                    jsonObject[jsonName] = setter.ToJson();
                }
            }
        }

        return node;
    }
}

public sealed class EmptyTask : RenderTask
{
    public EmptyTask()
    {
    }

    public override void Execute(in RenderTaskExecuteArgs args)
    {
    }
}
