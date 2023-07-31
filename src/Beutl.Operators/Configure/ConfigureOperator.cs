using Beutl.Animation;
using Beutl.Media;
using Beutl.Operation;
using Beutl.Rendering;
using Beutl.Styling;

namespace Beutl.Operators.Configure;

public abstract class ConfigureOperator<TTarget, TValue> : StylingOperator, ISourceTransformer
    where TTarget : Renderable
    where TValue : CoreObject, IAffectsRender, new()
{
    private IStyleInstance? _instance;
    private bool _transforming;

    public ConfigureOperator()
    {
        Value = new TValue();
        Value.Invalidated += (_, e) =>
        {
            if (!_transforming)
            {
                RaiseInvalidated(e);
            }
        };
    }

    protected TValue Value { get; }

    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<TValue>();
        style.Setters.AddRange(setters());
        return style;
    }

    [Obsolete]
    protected override void OnInitializeSetters(IList<ISetter> initializing)
    {
        base.OnInitializeSetters(initializing);
        foreach (CoreProperty item in GetProperties())
        {
            Type setterType = typeof(Setter<>).MakeGenericType(item.PropertyType);
            ICorePropertyMetadata metadata = item.GetMetadata<TValue, ICorePropertyMetadata>();
            object? defaultValue = metadata.GetDefaultValue();
            object? setter = defaultValue != null
                ? Activator.CreateInstance(setterType, item, defaultValue)
                : Activator.CreateInstance(setterType, item);

            if (setter is ISetter setter1)
            {
                initializing.Add(setter1);
            }
        }
    }

    public void Transform(IList<Renderable> value, IClock clock)
    {
        try
        {
            _transforming = true;
            if (!ReferenceEquals(Style, _instance?.Source) || _instance == null)
            {
                _instance?.Dispose();
                _instance = Style.Instance(Value);
            }

            if (_instance != null && IsEnabled)
            {
                _instance.Begin();
                _instance.Apply(clock);
                _instance.End();
            }

            foreach (TTarget item in value.OfType<TTarget>())
            {
                PreProcess(item, Value);
                Process(item, Value);
                PostProcess(item, Value);
            }
        }
        finally
        {
            _transforming = false;
        }
    }

    protected virtual void PreProcess(TTarget target, TValue value)
    {
    }

    protected virtual void PostProcess(TTarget target, TValue value)
    {
    }

    protected abstract void Process(TTarget target, TValue value);

    [Obsolete]
    protected virtual IEnumerable<CoreProperty> GetProperties()
    {
        yield break;
    }
}
