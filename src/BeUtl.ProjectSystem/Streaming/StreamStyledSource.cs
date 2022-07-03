using BeUtl.Animation;
using BeUtl.Rendering;
using BeUtl.Styling;

namespace BeUtl.Streaming;

#pragma warning disable IDE0032
public interface ISetterDescription
{
    bool? IsAnimatable { get; }

    IObservable<string>? Header { get; }

    CoreProperty Property { get; }

    object? DefaultValue { get; }

    ISetter ToSetter(StreamOperator streamOperator);
}

public record SetterDescription<T>(CoreProperty<T> Property) : ISetterDescription
{
    private bool? _isAnimatable;
    private IObservable<string>? _header;
    private T? _defaultValue;
    private T? _minimum;
    private T? _maximum;

    public bool? IsAnimatable
    {
        get => _isAnimatable;
        init => _isAnimatable = value;
    }

    public IObservable<string>? Header
    {
        get => _header;
        init => _header = value;
    }

    public T? DefaultValue
    {
        get => _defaultValue;
        init
        {
            _defaultValue = value;
            HasDefaultValue = true;
        }
    }

    public T? Minimum
    {
        get => _minimum;
        init
        {
            _minimum = value;
            HasMinimum = true;
        }
    }

    public T? Maximum
    {
        get => _maximum;
        init
        {
            _maximum = value;
            HasMinimum = true;
        }
    }

    public bool HasDefaultValue { get; private set; }

    public bool HasMinimum { get; private set; }

    public bool HasMaximum { get; private set; }

    CoreProperty ISetterDescription.Property => Property;

    object? ISetterDescription.DefaultValue => HasDefaultValue ? DefaultValue : null;

    public ISetter ToSetter(StreamOperator streamOperator)
    {
        return new InternalSetter(Property, DefaultValue, this, streamOperator);
    }

    internal sealed class InternalSetter : Setter<T>
    {
        public InternalSetter(CoreProperty<T> property, T? value, SetterDescription<T> description, StreamOperator streamOperator)
            : base(property, value)
        {
            Description = description;
            StreamOperator = streamOperator;
        }

        public SetterDescription<T> Description { get; }

        public StreamOperator StreamOperator { get; }
    }
}

public abstract class StreamStyledSource : StylingOperator, IStreamSource
{
    public virtual IRenderable? Publish(IClock clock)
    {
        OnPrePublish();
        IRenderable? renderable = null;

        if (ReferenceEquals(Style, Instance?.Source) || Instance?.Target == null)
        {
            renderable = Activator.CreateInstance(Style.TargetType) as IRenderable;
            if (renderable is IStyleable styleable)
            {
                Instance = Style.Instance(styleable);
            }
            else
            {
                renderable = null;
            }
        }

        if (Instance != null && IsEnabled)
        {
            Instance.Begin();
            Instance.Apply(clock);
            Instance.End();
        }

        OnPostPublish();

        return IsEnabled ? renderable : null;
    }

    protected virtual void OnPrePublish()
    {
    }

    protected virtual void OnPostPublish()
    {
    }
}
