using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Beutl;

public static class TypeDisplayHelpers
{
    private record DisplayInfo(string Name, string? Description);

    static TypeDisplayHelpers()
    {
        TypeUnloadNotifier.TypesUnloading += Unregister;
    }

    private static readonly ConditionalWeakTable<Type, DisplayInfo> s_displayCache = new();
    private static readonly ConditionalWeakTable<MemberInfo, DisplayInfo> s_memberDisplayCache = new();

    private static DisplayInfo GetDisplayInfo(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return s_displayCache.GetOrAdd(type, t =>
        {
            var displayAttribute = t.GetCustomAttribute<DisplayAttribute>();
            var name = displayAttribute?.GetName() ?? t.Name;
            var description = displayAttribute?.GetDescription();
            return new DisplayInfo(name, description);
        });
    }

    private static DisplayInfo GetDisplayInfo(MemberInfo member)
    {
        return s_memberDisplayCache.GetOrAdd(member, m =>
        {
            var displayAttribute = m.GetCustomAttribute<DisplayAttribute>();
            var name = displayAttribute?.GetName() ?? m.Name;
            var description = displayAttribute?.GetDescription();
            return new DisplayInfo(name, description);
        });
    }

    public static string GetLocalizedName(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return GetDisplayInfo(type).Name;
    }

    public static string? GetLocalizedDescription(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return GetDisplayInfo(type).Description;
    }

    public static string GetLocalizedName(MemberInfo member)
    {
        ArgumentNullException.ThrowIfNull(member);

        return GetDisplayInfo(member).Name;
    }

    public static string? GetLocalizedDescription(MemberInfo member)
    {
        ArgumentNullException.ThrowIfNull(member);

        return GetDisplayInfo(member).Description;
    }

    private static void Unregister(Type[] types)
    {
        foreach (Type type in types)
        {
            s_displayCache.Remove(type);
            foreach (var kvp in s_memberDisplayCache.Where(kvp => kvp.Key.DeclaringType == type))
            {
                s_memberDisplayCache.Remove(kvp.Key);
            }
        }
    }
}
