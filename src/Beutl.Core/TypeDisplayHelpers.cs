using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Beutl;

public static class TypeDisplayHelpers
{
    private static readonly ConcurrentDictionary<Type, string> s_typeNameCache = new();
    private static readonly ConcurrentDictionary<MemberInfo, string> s_memberNameCache = new();

    public static string GetLocalizedName(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (s_typeNameCache.TryGetValue(type, out string? cachedName))
        {
            return cachedName;
        }

        var displayAttribute = type.GetCustomAttributes(typeof(DisplayAttribute), false)
            .FirstOrDefault() as DisplayAttribute;
        string name = displayAttribute?.GetName() ?? type.Name;

        s_typeNameCache.TryAdd(type, name);
        return name;
    }

    public static string GetLocalizedName(MemberInfo member)
    {
        ArgumentNullException.ThrowIfNull(member);

        if (s_memberNameCache.TryGetValue(member, out string? cachedName))
        {
            return cachedName;
        }

        var displayAttribute = member.GetCustomAttribute<DisplayAttribute>();
        string name = displayAttribute?.GetName() ?? member.Name;

        s_memberNameCache.TryAdd(member, name);
        return name;
    }
}
