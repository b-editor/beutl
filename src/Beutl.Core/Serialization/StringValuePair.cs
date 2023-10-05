using System.Runtime.InteropServices;

namespace Beutl.Serialization;

[StructLayout(LayoutKind.Sequential)]
internal class StringValuePair<T>
{
    private string _key; // Do not rename (binary serialization)
    private T _value; // Do not rename (binary serialization)

    // Constructs a new DictionaryEnumerator by setting the Key
    // and Value fields appropriately.
    public StringValuePair(string key, T value)
    {
        _key = key;
        _value = value;
    }

    public string Key
    {
        get => _key;
        set => _key = value;
    }

    public T Value
    {
        get => _value;
        set => _value = value;
    }
}
