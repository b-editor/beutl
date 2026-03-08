namespace Beutl;

public static class TypeUnloadNotifier
{
    public static event Action<Type[]>? TypesUnloading;

    public static void NotifyUnloading(Type[] types)
    {
        ArgumentNullException.ThrowIfNull(types);
        TypesUnloading?.Invoke(types);
    }

    // typeがtargetと一致するか、ジェネリック引数に再帰的にtargetを含むかチェック
    public static bool ContainsTypeRecursive(Type type, Type target)
    {
        if (type == target) return true;
        if (type.IsGenericType)
        {
            foreach (Type arg in type.GetGenericArguments())
            {
                if (ContainsTypeRecursive(arg, target)) return true;
            }
        }

        return false;
    }
}
