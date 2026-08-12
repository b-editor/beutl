using System.Reflection;
using System.Runtime.Loader;

using NuGet.Packaging;

namespace Beutl.Api.Services;

public class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver = new(AppContext.BaseDirectory);
    private readonly PluginDependencyResolver _pluginResolver;
    private readonly TrustedPackageSnapshot? _trustedSnapshot;

    public PluginLoadContext(string mainDirectory, PackageFolderReader? reader = null)
        : this(mainDirectory, reader, trustedSnapshot: null)
    {
    }

    internal PluginLoadContext(
        string mainDirectory,
        PackageFolderReader? reader,
        TrustedPackageSnapshot? trustedSnapshot)
        : base(isCollectible: true)
    {
        _pluginResolver = new PluginDependencyResolver(mainDirectory, reader);
        _trustedSnapshot = trustedSnapshot;
    }

    internal Assembly LoadPackageAssembly(string assemblyPath)
    {
        return LoadAssemblyFromPathOrSnapshot(assemblyPath);
    }

    protected override Assembly? Load(AssemblyName name)
    {
        string? assemblyPath = _resolver.ResolveAssemblyToPath(name);
        if (assemblyPath != null)
        {
            return LoadAssemblyFromPathOrSnapshot(assemblyPath);
        }

        if (!CoreLibraries.IncludedInRuntimeDependencies(name.Name!, name.Version))
        {
            assemblyPath = _pluginResolver.ResolveAssemblyToPath(name);
            if (assemblyPath != null)
            {
                return LoadAssemblyFromPathOrSnapshot(assemblyPath);
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

    private Assembly LoadAssemblyFromPathOrSnapshot(string assemblyPath)
    {
        if (_trustedSnapshot?.TryGetAssemblyBytes(assemblyPath, out byte[] bytes) == true)
        {
            using var stream = new MemoryStream(bytes, writable: false);
            Assembly assembly = LoadFromStream(stream);
            _trustedSnapshot.RegisterLoadedAssembly(assembly);
            return assembly;
        }

        if (_trustedSnapshot?.IsPathInsidePackage(assemblyPath) == true)
        {
            throw new InvalidDataException("Verified package assembly was not present in its immutable snapshot.");
        }

        return LoadFromAssemblyPath(assemblyPath);
    }
}
