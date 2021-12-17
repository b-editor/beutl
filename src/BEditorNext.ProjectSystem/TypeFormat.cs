namespace BEditorNext;

internal static class TypeFormat
{
    public static Type? ToType(string fullName)
    {
        string[] arr = fullName.Split(':');

        if (arr.Length == 1)
        {
            return Type.GetType(arr[0]);
        }
        else if (arr.Length == 2)
        {
            return Type.GetType($"{arr[0]}, {arr[1]}");
        }
        else
        {
            return null;
        }
    }

    public static string ToString(Type type)
    {
        string? asm = type.Assembly.GetName().Name;
        string tname = type.FullName!;

        if (asm == null)
        {
            return tname;
        }
        else
        {
            return $"{tname}:{asm}";
        }
    }
}
