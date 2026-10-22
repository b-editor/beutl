namespace Beutl;

// What the property system reads from a ValueOnlyProperty<T> without its value type.
internal interface IValueOnlyProperty
{
    bool KeepsList { get; }
}
