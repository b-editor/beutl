using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace Beutl.Api.Services;

public class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    private readonly Dictionary<string, string> _assemblyPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _nativeSearchPaths = new();
    private readonly List<string> _resourceSearchPaths = new();
    private readonly string[] _assemblyDirectorySearchPaths;

    public PluginLoadContext(string specFile) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(AppContext.BaseDirectory);

        NuGetFramework framework = Helper.GetFrameworkName();

        var directory = Path.GetDirectoryName(specFile)!;
        var reader = new PackageFolderReader(directory);
        var availablePackages = new HashSet<PackageIdentity>();

        GetPackageDependencies(
            directory,
            reader,
            reader.GetIdentity(),
            framework,
            NullLogger.Instance,
            availablePackages);

        _assemblyDirectorySearchPaths = new string[1] { directory };
    }

    bool IsValidCulture(string name)
    {
        try
        {
            CultureInfo.GetCultureInfo(name);
            return true;
        }
        catch
        {
            return false;
        }
    }

    void GetPackageDependencies(
        string path,
        PackageFolderReader reader,
        PackageIdentity package,
        NuGetFramework framework,
        ILogger logger,
        ISet<PackageIdentity> availablePackages)
    {
        if (availablePackages.Contains(package)) return;

        var deps = reader.GetPackageDependencies();
        var nearest = Helper.FrameworkReducer.GetNearest(
            framework,
            deps.Select(x => x.TargetFramework));

        var libItems = reader.GetLibItems()
            .Where(x => x.TargetFramework == nearest)
            .SelectMany(x => x.Items)
            .ToArray();
        foreach (string item in libItems)
        {
            _assemblyPaths.TryAdd(
                Path.GetFileNameWithoutExtension(item),
                Path.Combine(path, item));
        }

        foreach (string item in libItems)
        {
            string? parent = Path.GetDirectoryName(item);
            string? culture = Path.GetFileNameWithoutExtension(parent);
            if (parent is { }
                && !_resourceSearchPaths.Contains(parent)
                && item.EndsWith(".resources.dll")
                && culture is { }
                && IsValidCulture(culture))
            {
                _resourceSearchPaths.Add(parent);
            }
        }

        foreach (string? item in reader.GetItems("runtimes")
            .SelectMany(x => x.Items))
        {
            string add = Path.Combine(path, item, "native");
            if (Directory.Exists(add))
            {
                _nativeSearchPaths.Add(add);
            }
        }

        availablePackages.Add(package);

        foreach (PackageDependency? dependency in deps.Where(x => x.TargetFramework == nearest)
            .SelectMany(x => x.Packages))
        {
            var dependentPackage = new PackageIdentity(dependency.Id, dependency.VersionRange.MinVersion);
            path = Helper.PackagePathResolver.GetInstalledPath(dependentPackage);
            reader = new PackageFolderReader(path);

            GetPackageDependencies(
                path,
                reader,
                dependentPackage,
                framework, logger, availablePackages);
        }
    }

    private const string NeutralCultureName = "neutral";
    private const string ResourceAssemblyExtension = ".dll";

    private string? ResolveAssemblyToPath(AssemblyName assemblyName)
    {
        if (!string.IsNullOrEmpty(assemblyName.CultureName)
            &&!string.Equals(assemblyName.CultureName, NeutralCultureName, StringComparison.OrdinalIgnoreCase))
        {
            foreach (string searchPath in _resourceSearchPaths)
            {
                string assemblyPath = Path.Combine(
                    searchPath,
                    assemblyName.CultureName,
                    $"{assemblyName.Name}{ResourceAssemblyExtension}");
                if (File.Exists(assemblyPath))
                {
                    return assemblyPath;
                }
            }
        }
        else if (assemblyName.Name != null)
        {
            if (_assemblyPaths.TryGetValue(assemblyName.Name, out string? assemblyPath))
            {
                if (File.Exists(assemblyPath))
                {
                    return assemblyPath;
                }
            }
        }

        return null;
    }

    protected override Assembly? Load(AssemblyName name)
    {
        string? assemblyPath = _resolver.ResolveAssemblyToPath(name);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        assemblyPath = ResolveAssemblyToPath(name);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        return null;
    }

    public string? ResolveUnmanagedDllToPath(string unmanagedDllName)
    {
        ArgumentNullException.ThrowIfNull(unmanagedDllName);

        IEnumerable<string> searchPaths;
        if (unmanagedDllName.Contains(Path.DirectorySeparatorChar))
        {
            // Library names with absolute or relative path can't be resolved
            // using the component .deps.json as that defines simple names.
            // So instead use the component directory as the lookup path.
            searchPaths = _assemblyDirectorySearchPaths;
        }
        else
        {
            searchPaths = _nativeSearchPaths;
        }

        bool isRelativePath = !Path.IsPathFullyQualified(unmanagedDllName);
        foreach (LibraryNameVariation libraryNameVariation in LibraryNameVariation.DetermineLibraryNameVariations(unmanagedDllName, isRelativePath))
        {
            string libraryName = libraryNameVariation.Prefix + unmanagedDllName + libraryNameVariation.Suffix;
            foreach (string searchPath in searchPaths)
            {
                string libraryPath = Path.Combine(searchPath, libraryName);
                if (File.Exists(libraryPath))
                {
                    return libraryPath;
                }
            }
        }

        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        libraryPath = ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        return IntPtr.Zero;
    }
}
