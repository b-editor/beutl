using System.Reflection;
using System.Runtime.Loader;

using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;

namespace Beutl.Api.Services;

// https://github.com/dotnet/runtime/blob/9ec7fc21862f3446c6c6f7dcfff275942e3884d3/src/libraries/System.Private.CoreLib/src/System/Runtime/Loader/AssemblyDependencyResolver.cs
internal sealed class PluginDependencyResolver
{
    private const string NeutralCultureName = "neutral";
    private const string ResourceAssemblyExtension = ".dll";

    private readonly Dictionary<string, string> _assemblyPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _nativeSearchPaths = [];
    private readonly HashSet<string> _resourceSearchPaths = [];
    private readonly string[] _assemblyDirectorySearchPaths;

    public PluginDependencyResolver(string mainDirectory, PackageFolderReader? reader)
    {
        NuGetFramework framework = Helper.GetFrameworkName();

        if (reader != null)
        {
            var availablePackages = new HashSet<PackageIdentity>();

            GetPackageDependencies(
                mainDirectory,
                reader,
                reader.GetIdentity(),
                framework,
                NullLogger.Instance,
                availablePackages);
        }
        else
        {
            foreach (string item in Directory.GetFiles(mainDirectory, "*.*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(mainDirectory, item);
                if (relative.StartsWith("runtimes")
                    && Path.GetDirectoryName(item) is { } fullname)
                {
                    _nativeSearchPaths.Add(fullname);
                }
                else if (item.EndsWith(".resources.dll"))
                {
                    string? parent = Path.GetDirectoryName(item);
                    string? culture = Path.GetFileName(parent);
                    if (parent is { }
                        && !_resourceSearchPaths.Contains(parent)
                        && culture is { }
                        && CultureNameValidation.IsValid(culture))
                    {
                        _resourceSearchPaths.Add(parent);
                    }
                }
                else if (item.EndsWith(".dll"))
                {
                    _assemblyPaths.TryAdd(Path.GetFileNameWithoutExtension(item), item);
                }
            }
        }

        _assemblyDirectorySearchPaths = [mainDirectory];
    }

    private void GetPackageDependencies(
        string path,
        PackageFolderReader reader,
        PackageIdentity package,
        NuGetFramework framework,
        ILogger logger,
        ISet<PackageIdentity> availablePackages)
    {
        if (availablePackages.Contains(package)) return;

        IEnumerable<PackageDependencyGroup> deps = reader.GetPackageDependencies();
        NuGetFramework? nearest = Helper.FrameworkReducer.GetNearest(
            framework,
            deps.Select(x => x.TargetFramework));

        string[] libItems = reader.GetLibItems()
            .Where(x => x.TargetFramework == nearest)
            .SelectMany(x => x.Items)
            .ToArray();
        foreach (string item in libItems)
        {
            if (item.EndsWith(".resources.dll"))
            {
                string? parent = Path.GetDirectoryName(Path.Combine(path, item));
                string? culture = Path.GetFileName(parent);
                if (parent is { }
                    && !_resourceSearchPaths.Contains(parent)
                    && culture is { }
                    && CultureNameValidation.IsValid(culture))
                {
                    _resourceSearchPaths.Add(parent);
                    continue;
                }
            }

            if (item.EndsWith(".dll"))
            {
                _assemblyPaths.TryAdd(
                    Path.GetFileNameWithoutExtension(item),
                    Path.Combine(path, item));
            }
        }

        foreach (string? item in reader.GetItems("runtimes")
            .SelectMany(x => x.Items))
        {
            string add = Path.Combine(path, Path.GetDirectoryName(item)!);
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
            if (path != null)
            {
                reader = new PackageFolderReader(path);

                GetPackageDependencies(
                    path,
                    reader,
                    dependentPackage,
                    framework, logger, availablePackages);
            }
        }
    }

    public string? ResolveAssemblyToPath(AssemblyName assemblyName)
    {
        if (!string.IsNullOrEmpty(assemblyName.CultureName)
            && !string.Equals(assemblyName.CultureName, NeutralCultureName, StringComparison.OrdinalIgnoreCase))
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
}
