namespace Beutl.ProjectSystem;

internal static class SerializedGraphTraversal
{
    public static IEnumerable<object> Enumerate(object root)
    {
        var result = new List<object>();
        Visit(root, "$", (value, _) =>
        {
            result.Add(value);
            return false;
        });
        return result;
    }

    public static bool Visit(object? root, string rootPath, Func<object, string, bool> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        foreach ((object value, SerializedGraphPath path) in SerializedGraphPaths.EnumerateTraversal(root))
        {
            if (visitor(value, rootPath + path.Positional[1..])) return true;
        }
        return false;
    }
}
