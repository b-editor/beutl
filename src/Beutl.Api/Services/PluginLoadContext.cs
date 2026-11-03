using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

using NuGet.Packaging;

namespace Beutl.Api.Services;

public class PluginLoadContext(string mainDirectory, PackageFolderReader? reader = null) : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new AssemblyDependencyResolver(AppContext.BaseDirectory);
    private readonly PluginDependencyResolver _pluginResolver = new PluginDependencyResolver(mainDirectory, reader);

    // The plugin each assembly in the default context came from, which knows where its native libraries are.
    private static readonly ConcurrentDictionary<Assembly, PluginDependencyResolver> s_defaultContextAssemblies = new();

    // Each context looks for a copy in the other before it loads its own. Two threads that looked at once would both
    // find nothing, and the assembly's types would have two identities.
    private static readonly Lock s_loadGate = new();

    static PluginLoadContext()
    {
        // A plugin or a library it uses can load an assembly through AssemblyLoadContext.Default,
        // which looks next to Beutl and not in the plugin's packages.
        Default.Resolving += static (_, name) => ResolveForDefaultContext(name);
        Default.ResolvingUnmanagedDll += static (assembly, name) => ResolveNativeForDefaultContext(assembly, name);
    }

    protected override Assembly? Load(AssemblyName name)
    {
        string? assemblyPath = _resolver.ResolveAssemblyToPath(name);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        // A copy that went to the default context takes the place of this plugin's, so that its types keep one identity.
        assemblyPath = ResolvePluginAssemblyToPath(name);
        if (assemblyPath != null)
        {
            lock (s_loadGate)
            {
                if (!Default.Assemblies.Any(assembly => Satisfies(assembly.GetName(), name)))
                {
                    return LoadFromAssemblyPath(assemblyPath);
                }
            }
        }

        return base.Load(name);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        libraryPath = _pluginResolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        return IntPtr.Zero;
    }

    private string? ResolvePluginAssemblyToPath(AssemblyName name)
    {
        string? assemblyPath = _pluginResolver.ResolveAssemblyToPath(name);
        return assemblyPath != null && !CoreLibraries.IncludedInRuntimeDependencies(name.Name!, name.Version)
            ? assemblyPath
            : null;
    }

    // The default context cannot bind an assembly of a collectible context, so it gets its own copy, which stays
    // loaded until Beutl exits. An assembly that a plugin has loaded stays missing there: a second copy would give
    // that plugin's types a second identity.
    private static Assembly? ResolveForDefaultContext(AssemblyName name)
    {
        lock (s_loadGate)
        {
            PluginLoadContext[] contexts = [.. All.OfType<PluginLoadContext>()];
            if (contexts.Any(context => context.Assemblies.Any(assembly => Satisfies(assembly.GetName(), name))))
            {
                return null;
            }

            foreach (PluginLoadContext context in contexts)
            {
                string? assemblyPath = context.ResolvePluginAssemblyToPath(name);
                if (assemblyPath != null && Satisfies(AssemblyName.GetAssemblyName(assemblyPath), name))
                {
                    try
                    {
                        Assembly assembly = Default.LoadFromAssemblyPath(assemblyPath);
                        s_defaultContextAssemblies[assembly] = context._pluginResolver;
                        return assembly;
                    }
                    catch (FileLoadException)
                    {
                        // It already holds an older copy of this name, which it cannot replace. The name stays missing,
                        // which is what code that probes for an optional assembly expects to hear.
                        return null;
                    }
                }
            }

            return null;
        }
    }

    private static IntPtr ResolveNativeForDefaultContext(Assembly assembly, string name)
    {
        return s_defaultContextAssemblies.TryGetValue(assembly, out PluginDependencyResolver? resolver)
               && resolver.ResolveUnmanagedDllToPath(name) is { } path
            ? NativeLibrary.Load(path)
            : IntPtr.Zero;
    }

    private static bool Satisfies(AssemblyName definition, AssemblyName reference)
    {
        return string.Equals(definition.Name, reference.Name, StringComparison.OrdinalIgnoreCase)
               && string.Equals(definition.CultureName ?? "", reference.CultureName ?? "", StringComparison.OrdinalIgnoreCase)
               && (reference.Version == null || definition.Version >= reference.Version);
    }
}
