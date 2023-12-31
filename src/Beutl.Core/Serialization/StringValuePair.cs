using System.Runtime.InteropServices;

namespace Beutl.Serialization;

[StructLayout(LayoutKind.Sequential)]
internal class StringValuePair<T>(string key, T value)
{
    public string Key { get; set; } = key;

    public T Value { get; set; } = value;
}
