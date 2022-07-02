using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

using BeUtl.Animation;

namespace BeUtl.Styling;

public abstract class Styleable : Element, IStyleable
{
    public static readonly CoreProperty<Styles> StylesProperty;
    private readonly Styles _styles;
    private IStyleInstance? _styleInstance;

    static Styleable()
    {
        StylesProperty = ConfigureProperty<Styles, Styleable>(nameof(Styles))
            .Accessor(o => o.Styles, (o, v) => o.Styles = v)
            .Register();
    }

    protected Styleable()
    {
        _styles = new()
        {
            Attached = item => item.Invalidated += Style_Invalidated,
            Detached = item => item.Invalidated -= Style_Invalidated
        };
        _styles.CollectionChanged += Style_Invalidated;
    }

    private void Style_Invalidated(object? sender, EventArgs e)
    {
        _styleInstance = null;
    }

    public Styles Styles
    {
        get => _styles;
        set
        {
            if (_styles != value)
            {
                _styles.Replace(value);
            }
        }
    }

    public void InvalidateStyles()
    {
        if (_styleInstance != null)
        {
            _styleInstance.Dispose();
            _styleInstance = null;
        }
    }

    public void ApplyStyling(IClock clock)
    {
        if (_styleInstance == null)
        {
            _styleInstance = Styles.Instance(this);
        }

        if (_styleInstance != null)
        {
            _styleInstance.Begin();
            _styleInstance.Apply(clock);
            _styleInstance.End();
        }
    }

    IStyleInstance? IStyleable.GetStyleInstance(IStyle style)
    {
        IStyleInstance? styleInstance = _styleInstance;
        while (styleInstance != null)
        {
            if (styleInstance.Source == style)
            {
                return styleInstance;
            }
        }

        return null;
    }

    void IStyleable.StyleApplied(IStyleInstance instance)
    {
        _styleInstance = instance;
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);
        if (json is JsonObject jobject)
        {
            if (jobject.TryGetPropertyValue("styles", out JsonNode? stylesNode)
                && stylesNode is JsonArray stylesArray)
            {
                Styles.Clear();
                if (Styles.Capacity < stylesArray.Count)
                {
                    Styles.Capacity = stylesArray.Count;
                }

                foreach (JsonNode? styleNode in stylesArray)
                {
                    if (styleNode is JsonObject styleObject
                        && styleObject.ToStyle() is Style style)
                    {
                        Styles.Add(style);
                    }
                }
            }
        }
    }

    public override void WriteToJson(ref JsonNode json)
    {
        base.WriteToJson(ref json);
        if (json is JsonObject jobject)
        {
            if (Styles.Count > 0)
            {
                var styles = new JsonArray();

                foreach (IStyle style in Styles.AsSpan())
                {
                    styles.Add(style.ToJson());
                }

                jobject["styles"] = styles;
            }
        }
    }

    private interface IGenericHelper
    {
        ISetter InitializeSetter(CoreProperty property, object? value, IEnumerable<BaseAnimation> animations);

        BaseAnimation DeserializeAnimation(JsonObject json);
    }

    private sealed class GenericHelper<T> : IGenericHelper
    {
        public static readonly GenericHelper<T> Instance = new();

        public ISetter InitializeSetter(CoreProperty property, object? value, IEnumerable<BaseAnimation> animations)
        {
            var setter = new Setter<T>((CoreProperty<T>)property, (T?)value);
            setter.Animations.AddRange(animations.OfType<Animation<T>>());
            return setter;
        }

        public BaseAnimation DeserializeAnimation(JsonObject json)
        {
            var anm = new Animation<T>();
            anm.ReadFromJson(json);
            return anm;
        }
    }
}
