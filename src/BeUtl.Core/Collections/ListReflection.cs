using System.Diagnostics.CodeAnalysis;

namespace Beutl.Collections;

internal class ListReflection<T>
{
    [AllowNull]
    public T[] Items;
    public int Count;
    public int Version;
}
