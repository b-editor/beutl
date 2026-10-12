using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Beutl.JsonDiscriminator;
using Beutl.Logging;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl;

internal static class TypeFormat
{
    private static readonly object s_sync = new();
    private static readonly object s_marker = new();

    // Names that resolved without a former name. A name that does not resolve is not kept, so it resolves once its
    // assembly loads, and neither is one that needed a former name, so a type with that name takes over once it loads.
    private static readonly ConcurrentDictionary<string, Type> s_resolved = new(StringComparer.Ordinal);

    // The type that gives each former name (FormerPersistedNameAttribute), keyed by AliasKey, from the assemblies
    // scanned so far. Guarded by s_sync.
    private static readonly Dictionary<string, Type> s_aliases = new(StringComparer.Ordinal);
    private static readonly ConditionalWeakTable<Assembly, object> s_scannedAssemblies = new();

    // Types TypeUnloadNotifier announced. Their assembly stays listed until its load context is collected, so a name
    // that holds one stops resolving, and they are neither kept in s_resolved nor scanned for former names again.
    // Written under s_sync, with s_unloadGeneration.
    private static readonly ConditionalWeakTable<Type, object> s_unloadedTypes = new();
    private static int s_unloadGeneration;

    private static ILogger? s_logger;

    private static ILogger Logger => Log.GetLoggerOnceConfigured(ref s_logger, typeof(TypeFormat));

    public static Type? ToType(string fullName)
    {
        if (s_resolved.TryGetValue(fullName, out Type? resolved))
        {
            return resolved;
        }

        int generation = Volatile.Read(ref s_unloadGeneration);
        Type? type = Parse(fullName, out bool usedFormerName);
        // The parser still finds an unloaded type while its load context waits to be collected. Handing it out
        // would bring the plugin's type back and keep the context alive.
        if (type == null || ContainsType(type, IsUnloaded))
        {
            return null;
        }

        if (!usedFormerName)
        {
            lock (s_sync)
            {
                // Not kept when a type it holds was unloaded while this name was being parsed.
                if (generation == s_unloadGeneration && !ContainsType(type, IsUnloaded))
                {
                    s_resolved.TryAdd(fullName, type);
                }
            }
        }

        return type;
    }

    private static Type? Parse(string fullName, out bool usedFormerName)
    {
        usedFormerName = false;
        try
        {
            List<Token> tokens = new TypeNameTokenizer(fullName).Tokenize();
            var parser = new TypeNameParser(tokens);
            Type? type = parser.Parse();
            usedFormerName = parser.UsedFormerName;
            return type;
        }
        // The tokenizer/parser index freely and throw on ill-formed names (e.g. "x" from a
        // hand-edited file); every caller already treats null as "unknown type".
        catch (Exception ex) when (ex is IndexOutOfRangeException
                                       or ArgumentOutOfRangeException
                                       or ArgumentException
                                       or InvalidOperationException)
        {
            return null;
        }
    }

    public static string ToString(Type type)
    {
        return new TypeNameFormatter(type).Format();
    }

    // The former names FormerPersistedNameAttribute gives type. One that is not written as the attribute describes
    // is left out and logged.
    internal static IReadOnlyList<FormerTypeName> GetFormerNames(Type type)
    {
        object[] attributes;
        try
        {
            if (!type.IsDefined(typeof(FormerPersistedNameAttribute), inherit: false))
            {
                return [];
            }

            attributes = type.GetCustomAttributes(typeof(FormerPersistedNameAttribute), inherit: false);
        }
        catch (Exception ex)
        {
            // Another attribute of a plugin type can come from an assembly that does not load.
            Logger.LogDebug(ex, "Could not read the attributes of {Type}.", type);
            return [];
        }

        int arityIndex = type.Name.IndexOf('`');
        string aritySuffix = arityIndex < 0 ? "" : type.Name[arityIndex..];
        var names = new List<FormerTypeName>(attributes.Length);
        foreach (FormerPersistedNameAttribute attribute in attributes.OfType<FormerPersistedNameAttribute>())
        {
            if (!string.IsNullOrWhiteSpace(attribute.Name)
                && TypeNameParser.ParseFormerName(attribute.Name, aritySuffix) is { } name)
            {
                names.Add(name);
            }
            else
            {
                Logger.LogWarning(
                    "The former persisted name '{Name}' of {Type} is not written as [Assembly]Namespace:Type, so it is ignored.",
                    attribute.Name,
                    type);
            }
        }

        return names;
    }

    // The type that gives the type definition fullName in assemblyName as a former name, or null. TypeNameParser
    // calls this for a definition no type has under that name.
    internal static Type? FindAlias(string assemblyName, string fullName)
    {
        lock (s_sync)
        {
            ScanNewAssemblies();
            return s_aliases.GetValueOrDefault(AliasKey(assemblyName, fullName));
        }
    }

    // TypeUnloadNotifier calls this before raising its event, whether or not anything was resolved yet: the alias
    // table can be built later, from assemblies that are still listed then.
    internal static void ForgetUnloadedTypes(Type[] types)
    {
        if (types.Length == 0)
        {
            return;
        }

        var unloaded = new HashSet<Type>(types);
        Func<Type, bool> isUnloaded = unloaded.Contains;
        lock (s_sync)
        {
            s_unloadGeneration++;
            foreach (Type type in types)
            {
                s_unloadedTypes.AddOrUpdate(type, s_marker);
            }

            foreach (KeyValuePair<string, Type> entry in s_resolved)
            {
                if (ContainsType(entry.Value, isUnloaded))
                {
                    s_resolved.TryRemove(entry);
                }
            }

            string[] unloadedAliases = s_aliases
                .Where(entry => unloaded.Contains(entry.Value))
                .Select(entry => entry.Key)
                .ToArray();
            foreach (string key in unloadedAliases)
            {
                s_aliases.Remove(key);
            }
        }
    }

    private static string AliasKey(string assemblyName, string fullName) => $"[{assemblyName}]{fullName}";

    private static bool IsUnloaded(Type type) => s_unloadedTypes.TryGetValue(type, out _);

    // Whether type, its generic type definition or one of its type arguments matches.
    private static bool ContainsType(Type type, Func<Type, bool> match)
    {
        if (match(type))
        {
            return true;
        }

        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            if (match(type.GetGenericTypeDefinition()))
            {
                return true;
            }

            foreach (Type argument in type.GetGenericArguments())
            {
                if (ContainsType(argument, match))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Adds the former names of the types in each assembly loaded since the last scan.
    private static void ScanNewAssemblies()
    {
        Assembly core = typeof(FormerPersistedNameAttribute).Assembly;
        string? coreName = core.GetName().Name;
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (s_scannedAssemblies.TryGetValue(assembly, out _))
            {
                continue;
            }

            s_scannedAssemblies.AddOrUpdate(assembly, s_marker);
            try
            {
                ScanAssembly(assembly, core, coreName);
            }
            catch (Exception ex)
            {
                // A plugin assembly that cannot be read must not stop other names from resolving.
                Logger.LogWarning(ex, "Could not read the former persisted names in {Assembly}.", assembly.FullName);
            }
        }
    }

    private static void ScanAssembly(Assembly assembly, Assembly core, string? coreName)
    {
        // Only an assembly that references Beutl.Core can apply the attribute.
        if (assembly.IsDynamic
            || (assembly != core && !assembly.GetReferencedAssemblies().Any(name => name.Name == coreName)))
        {
            return;
        }

        foreach (Type type in GetLoadableTypes(assembly))
        {
            if (IsUnloaded(type))
            {
                continue;
            }

            foreach (FormerTypeName name in GetFormerNames(type))
            {
                string key = AliasKey(name.AssemblyName, name.FullName);
                if (!s_aliases.TryAdd(key, type) && s_aliases[key] != type)
                {
                    Logger.LogWarning(
                        "Ignoring the former persisted name {Name} of {Type}: {Other} gives it too.",
                        key,
                        type,
                        s_aliases[key]);
                }
            }
        }
    }

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>().ToArray();
        }
    }
}
