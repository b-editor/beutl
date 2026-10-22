namespace Beutl.Editor.Infrastructure;

// What a history property path such as "Opacity", "Transform.X" or "Opacity.Expression" changes.
internal enum PropertyPathSlot
{
    Value,
    Animation,
    Expression
}

// The one parser of the dotted paths that history operations, their observers and the trackers of edits read.
public class PropertyPathHelper
{
    public static string GetPropertyNameFromPath(string propertyPath)
    {
        return Parse(propertyPath).Name;
    }

    // A last name of Animation or Expression names that slot of the property before it.
    internal static (string Name, PropertyPathSlot Slot) Parse(string path)
    {
        int last = path.LastIndexOf('.');
        if (last < 0) return (path, PropertyPathSlot.Value);

        string name = path[(last + 1)..];
        PropertyPathSlot slot = name switch
        {
            nameof(PropertyPathSlot.Animation) => PropertyPathSlot.Animation,
            nameof(PropertyPathSlot.Expression) => PropertyPathSlot.Expression,
            _ => PropertyPathSlot.Value
        };
        if (slot == PropertyPathSlot.Value) return (name, slot);

        int previous = last > 0 ? path.LastIndexOf('.', last - 1) : -1;
        return (path[(previous + 1)..last], slot);
    }

    internal static string Append(string path, string name)
    {
        return string.IsNullOrEmpty(path) ? name : $"{path}.{name}";
    }

    // The first name below parent in path, or null when path is not below parent.
    internal static string? ChildName(string path, string parent)
    {
        if (parent.Length > 0)
        {
            if (path.Length <= parent.Length + 1
                || path[parent.Length] != '.'
                || !path.StartsWith(parent, StringComparison.Ordinal))
            {
                return null;
            }

            path = path[(parent.Length + 1)..];
        }

        int dot = path.IndexOf('.');
        string name = dot < 0 ? path : path[..dot];
        return name.Length > 0 ? name : null;
    }
}
