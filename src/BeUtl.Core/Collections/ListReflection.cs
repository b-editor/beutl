using System.Diagnostics.CodeAnalysis;

namespace BeUtl.Collections;

internal class ListReflection<T>
{
    [AllowNull]
    public T[] Items;
    public int Count;
    public int Version;
}
