using BeUtl.Styling;

namespace BeUtl.Streaming;

public record StyleSetterDescription<T>(CoreProperty<T> Property, Style DefaultValue) : ISetterDescription
{
    public bool? IsAnimatable { get; }

    public IObservable<string>? Header { get; init; }

    CoreProperty ISetterDescription.Property => Property;

    object? ISetterDescription.DefaultValue => DefaultValue;

    public ISetter ToSetter(StreamOperator streamOperator)
    {
        return new InternalSetter(Property, DefaultValue, this, streamOperator);
    }

    internal sealed class InternalSetter : StyleSetter<T>, ISetterDescription.IInternalSetter
    {
        public InternalSetter(CoreProperty<T> property, Style value, StyleSetterDescription<T> description, StreamOperator streamOperator)
            : base(property, value)
        {
            Description = description;
            StreamOperator = streamOperator;
        }

        public StyleSetterDescription<T> Description { get; }

        public StreamOperator StreamOperator { get; }

        ISetterDescription ISetterDescription.IInternalSetter.Description => Description;

        public void MigrateFrom(ISetter setter)
        {
            if (setter is StyleSetter<T> typed)
            {
                Property = typed.Property;
                Value = typed.Value;
            }
        }
    }
}
