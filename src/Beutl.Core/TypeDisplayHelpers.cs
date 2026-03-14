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

    private static readonly ConditionalWeakTable<Type, Tuple<string, string?>> s_displayCache = new();
    private static readonly ConditionalWeakTable<MemberInfo, Tuple<string, string?>> s_memberDisplayCache = new();

    public static string GetLocalizedName(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return s_displayCache.GetOrAdd(type, t =>
        {
            var displayAttribute = t.GetCustomAttribute<DisplayAttribute>();
            var name = displayAttribute?.GetName() ?? t.Name;
            var description = displayAttribute?.GetDescription();
            return Tuple.Create(name, description);
        }).Item1;
    }

    public static string? GetLocalizedDescription(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return s_displayCache.GetOrAdd(type, t =>
        {
            var displayAttribute = t.GetCustomAttribute<DisplayAttribute>();
            var name = displayAttribute?.GetName() ?? t.Name;
            var description = displayAttribute?.GetDescription();
            return Tuple.Create(name, description);
        }).Item2;
    }

    public static string GetLocalizedName(MemberInfo member)
    {
        ArgumentNullException.ThrowIfNull(member);

        return s_memberDisplayCache.GetOrAdd(member, m =>
        {
            var displayAttribute = m.GetCustomAttribute<DisplayAttribute>();
            var name = displayAttribute?.GetName() ?? m.Name;
            var description = displayAttribute?.GetDescription();
            return Tuple.Create(name, description);
        }).Item1;
    }

    public static string? GetLocalizedDescription(MemberInfo member)
    {
        ArgumentNullException.ThrowIfNull(member);

        return s_memberDisplayCache.GetOrAdd(member, m =>
        {
            var displayAttribute = m.GetCustomAttribute<DisplayAttribute>();
            var name = displayAttribute?.GetName() ?? m.Name;
            var description = displayAttribute?.GetDescription();
            return Tuple.Create(name, description);
        }).Item2;
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
