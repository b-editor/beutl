using System.Reflection;
using System.Runtime.Loader;

using NuGet.Packaging;

namespace Beutl.Api.Services;

// Todo: 最初に許可されたアセンブリのリストから探すようにする。
public class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly PluginDependencyResolver _pluginResolver;

    public PluginLoadContext(string mainDirectory, PackageFolderReader? reader = null) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(AppContext.BaseDirectory);
        _pluginResolver = new PluginDependencyResolver(mainDirectory, reader);
    }

    protected override Assembly? Load(AssemblyName name)
    {
        string? assemblyPath = _resolver.ResolveAssemblyToPath(name);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        if (!Helper.IsCoreLibraries(name.Name!))
        {
            assemblyPath = _pluginResolver.ResolveAssemblyToPath(name);
            if (assemblyPath != null)
            {
                return LoadFromAssemblyPath(assemblyPath);
            }
        }

        var a = base.Load(name);
        return a;
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
}
