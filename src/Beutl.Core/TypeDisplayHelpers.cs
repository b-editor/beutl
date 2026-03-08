using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Beutl;

public static class TypeDisplayHelpers
{
    static TypeDisplayHelpers()
    {
        TypeUnloadNotifier.TypesUnloading += Unregister;
    }

    private static readonly ConditionalWeakTable<Type, string> s_typeNameCache = new();
    private static readonly ConditionalWeakTable<MemberInfo, string> s_memberNameCache = new();

    public static string GetLocalizedName(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return s_typeNameCache.GetOrAdd(type, t =>
        {
            var displayAttribute = t.GetCustomAttribute<DisplayAttribute>();
            return displayAttribute?.GetName() ?? t.Name;
        });
    }

    public static string GetLocalizedName(MemberInfo member)
    {
        ArgumentNullException.ThrowIfNull(member);

        return s_memberNameCache.GetOrAdd(member, m =>
        {
            var displayAttribute = m.GetCustomAttribute<DisplayAttribute>();
            return displayAttribute?.GetName() ?? m.Name;
        });
    }

    private static void Unregister(Type[] types)
    {
        foreach (Type type in types)
        {
            s_typeNameCache.Remove(type);
            foreach (var kvp in s_memberNameCache.Where(kvp => kvp.Key.DeclaringType == type))
            {
                s_memberNameCache.Remove(kvp.Key);
            }
        }
    }
}
