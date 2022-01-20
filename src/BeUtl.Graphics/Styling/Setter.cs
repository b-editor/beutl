
using BeUtl.Animation;
using BeUtl.Collections;

namespace BeUtl.Styling;

public class Setter<T> : ISetter
{
    private CoreProperty<T>? _property;
    private CoreList<Animation<T>>? _animation;

    public Setter()
    {
    }

    public Setter(CoreProperty<T> property, T? value)
    {
        _property = property;
        Value = value;
    }

    public CoreProperty<T> Property
    {
        get => _property ?? throw new InvalidOperationException();
        set => _property = value;
    }

    public T? Value { get; set; }

    public ICoreList<Animation<T>> Animations => _animation ??= new CoreList<Animation<T>>();

    CoreProperty ISetter.Property => Property;

    object? ISetter.Value => Value;

    ICoreReadOnlyList<IAnimation> ISetter.Animations => Animations;

    public ISetterBatch CreateBatch(IStyleable target)
    {
        return new SetterBatch<T>(Property, target);
    }

    public ISetterInstance Instance(IStyleable target)
    {
        return new SetterInstance<T>(this);
    }
}
