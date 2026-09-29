using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
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
            PackageIdentity root = reader.GetIdentity();
            string packageDirectory = Path.GetDirectoryName(reader.GetNuspecFile())!;
            foreach (PackageIdentity package in ResolvedPackageDependencies.Load(reader, framework)
                         .OrderBy(package => PackageIdentityComparer.Default.Equals(package, root) ? 0 : 1))
            {
                if (PackageIdentityComparer.Default.Equals(package, root))
                {
                    AddPackageAssets(packageDirectory, reader, framework);
                }
                else
                {
                    string directory = Helper.ResolveInstalledDirectory(package);
                    using var dependencyReader = new PackageFolderReader(directory);
                    AddPackageAssets(directory, dependencyReader, framework);
                }
            }
        }
        else
        {
            string[] files = Directory.GetFiles(mainDirectory, "*.*", SearchOption.AllDirectories);
            foreach (string item in files)
            {
                string relative = Path.GetRelativePath(mainDirectory, item);
                if (relative.StartsWith("runtimes" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    continue;
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
            AddNativeSearchPaths(mainDirectory, files.Select(file => Path.GetRelativePath(mainDirectory, file)));
        }

        _assemblyDirectorySearchPaths = [mainDirectory];
    }

    private void AddPackageAssets(
        string path,
        PackageFolderReader reader,
        NuGetFramework framework)
    {
        FrameworkSpecificGroup[] libGroups = reader.GetLibItems().ToArray();
        NuGetFramework? nearest = Helper.FrameworkReducer.GetNearest(
            framework,
            libGroups.Select(group => group.TargetFramework));

        string[] libItems = libGroups
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

        AddNativeSearchPaths(path, reader.GetItems("runtimes").SelectMany(group => group.Items));
    }

    private void AddNativeSearchPaths(string directory, IEnumerable<string> files)
    {
        string[] paths = files.Select(path => path.Replace('\\', '/')).ToArray();
        foreach (string runtime in GetRuntimeIdentifiers().Distinct(StringComparer.Ordinal))
        {
            string prefix = $"runtimes/{runtime}/native/";
            string[] matches = paths.Where(path => path.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            if (matches.Length == 0) continue;
            foreach (string path in matches)
                _nativeSearchPaths.Add(Path.GetDirectoryName(Path.Combine(directory, path))!);
            // A package's best matching RID wins; do not load another architecture's
            // identically named library or mix generic assets into a more specific set.
            break;
        }
    }

    private static IEnumerable<string> GetRuntimeIdentifiers()
    {
        yield return RuntimeInformation.RuntimeIdentifier;
        string architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        string? os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : OperatingSystem.IsLinux() ? "linux" : null;
        if (os != null)
        {
            yield return $"{os}-{architecture}";
            yield return os;
        }
        if (!OperatingSystem.IsWindows())
        {
            yield return $"unix-{architecture}";
            yield return "unix";
        }
        yield return "any";
    }

    public string? ResolveAssemblyToPath(AssemblyName assemblyName)
    {
        if (!string.IsNullOrEmpty(assemblyName.CultureName)
            && !string.Equals(assemblyName.CultureName, NeutralCultureName, StringComparison.OrdinalIgnoreCase))
        {
            foreach (string searchPath in _resourceSearchPaths)
            {
                if (!string.Equals(Path.GetFileName(searchPath), assemblyName.CultureName, StringComparison.OrdinalIgnoreCase))
                    continue;
                string assemblyPath = Path.Combine(
                    searchPath,
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
