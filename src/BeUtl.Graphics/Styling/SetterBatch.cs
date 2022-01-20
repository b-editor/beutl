namespace BeUtl.Styling;

public sealed class SetterBatch<T> : ISetterBatch
{
    private bool _beginning;
    private T? _oldValue;

    public SetterBatch(CoreProperty<T> property, IStyleable target)
    {
        Value = (T?)property.GetMetadata(target.GetType()).DefaultValue;
        Property = property;
        Target = target;
    }

    public T? Value { get; set; }

    public CoreProperty<T> Property { get; }

    public IStyleable Target { get; }

    CoreProperty ISetterBatch.Property => Property;

    public void Apply()
    {
        if (Value != null)
        {
            Target.SetValue(Property, Value);
        }
    }

    public void Begin()
    {
        if (!_beginning)
        {
            _oldValue = Target.GetValue(Property);
            _beginning = true;
        }
    }

    public void End()
    {
        if (_beginning)
        {
            Target.SetValue(Property, _oldValue);
            _beginning = false;
        }
    }
}
