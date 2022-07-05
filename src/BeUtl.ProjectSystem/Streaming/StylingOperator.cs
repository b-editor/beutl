using System.Text.Json.Nodes;

using Avalonia.Collections.Pooled;

using BeUtl.Styling;

namespace BeUtl.Streaming;

public abstract class StylingOperator : StreamOperator
{
    protected StylingOperator()
    {
        Style = OnInitializeStyle(() =>
        {
            var list = new PooledList<ISetterDescription>();
            OnInitializeSetters(list);
            return list.ConvertAll(x => x.ToSetter(this));
        });
        Style.Invalidated += OnInvalidated;
    }

    public IStyle Style { get; }

    public IStyleInstance? Instance { get; protected set; }

    protected abstract Style OnInitializeStyle(Func<IList<ISetter>> setters);

    protected virtual void OnInitializeSetters(IList<ISetterDescription> initializing)
    {
    }

    private void OnInvalidated(object? s, EventArgs e)
    {
        RaiseInvalidated();
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);
        if (json is JsonObject obj
            && obj.TryGetPropertyValue("style", out JsonNode? styleNode)
            && styleNode is JsonObject styleObj)
        {
            var style = StyleSerializer.ToStyle(styleObj);
            if (style != null)
            {
                Style.Invalidated -= OnInvalidated;
                foreach (ISetter setter in Style.Setters)
                {
                    if (setter is ISetterDescription.IInternalSetter setter1
                        && style.Setters.FirstOrDefault(x => x.Property.Id == setter.Property.Id) is ISetter setter2)
                    {
                        setter1.Synchronize(setter2);
                    }
                }

                Style.Invalidated += OnInvalidated;

                RaiseInvalidated();
            }
        }
    }

    public override void WriteToJson(ref JsonNode json)
    {
        base.WriteToJson(ref json);
        if (json is JsonObject obj)
        {
            obj["style"] = StyleSerializer.ToJson(Style);
        }
    }
}
