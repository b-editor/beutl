using Beutl.Collections;

namespace Beutl.Serialization;

// Reads a list an object keeps as a base field, as a CoreProperty whose setter replaced the list's items read one.
internal static class KeptListReader
{
    // TList is the type the list was saved as; an unchanged list a populate preserved is left as it is.
    public static void ReadInto<TList, T>(ICoreSerializationContext context, string name, ICoreList<T> list)
        where TList : IList<T>
    {
        if (context is JsonSerializationContext json
            && json.TryGetPreservedValue(name, typeof(TList), out _, allowCollections: true))
        {
            return;
        }

        if (context.Contains(name) && context.GetValue<TList>(name) is { } items)
        {
            list.Replace(items);
        }
    }
}
