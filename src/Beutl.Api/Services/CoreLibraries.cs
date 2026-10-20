using System.Collections.Frozen;
using System.Reflection;

using Microsoft.Extensions.DependencyModel;

using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.Api.Services;

internal static class CoreLibraries
{
    private static readonly Lazy<FrozenDictionary<string, string[]>> s_runtimeMap
        = new(() => CollectRuntimeDependencies()
            .GroupBy(i => i.Name, i => i.Version)
            .ToFrozenDictionary(x => x.Key, x => x.ToArray()));
    private static readonly Lazy<FrozenDictionary<string, string[]>> s_pkgMap
        = new(() => CollectPackageDependencies()
            .GroupBy(i => i.Name, i => i.Version)
            .ToFrozenDictionary(x => x.Key, x => x.ToArray()));
    private static List<PackageIdentity>? s_preferredVersions;

    // Beutl's packed and host-resolved libraries, listed from the projects' marks; they always come from the host.
    private static readonly FrozenSet<string> s_beutlLibraries = ReadBeutlLibraries();

    public static bool IsBeutlLibrary(string name) => s_beutlLibraries.Contains(name);

    public static IEnumerable<PackageIdentity> GetPreferredVersions()
    {
        s_preferredVersions ??= CollectPackageDependencies()
            .Select(x => new PackageIdentity(x.Name, NuGetVersion.Parse(x.Version)))
            .ToList();

        return s_preferredVersions;
    }

    public static IEnumerable<Dependency> CollectPackageDependencies()
    {
        DependencyContext depsContext = LoadBeutlDependencyContext();

        var library = new HashSet<Dependency>();

        foreach (RuntimeLibrary lib in depsContext.RuntimeLibraries)
        {
            library.Add(new(lib.Name, lib.Version));
        }

        return library;
    }

    public static IEnumerable<Dependency> CollectRuntimeDependencies()
    {
        DependencyContext depsContext = LoadBeutlDependencyContext();

        var library = new HashSet<Dependency>();

        foreach (RuntimeLibrary lib in depsContext.RuntimeLibraries)
        {
            foreach (RuntimeAssetGroup group in lib.RuntimeAssemblyGroups)
            {
                foreach (RuntimeFile item in group.RuntimeFiles)
                {
                    string fileName = Path.GetFileNameWithoutExtension(item.Path);
                    string? version = item.AssemblyVersion;

                    // Beutl's own libraries have no version in the deps file, so they take the application's.
                    if (version == null && IsBeutlLibrary(fileName))
                    {
                        version = BeutlApplication.Version;
                    }

                    if (version != null)
                        library.Add(new Dependency(fileName, version));
                }
            }
        }

        return library;
    }

    private static DependencyContext LoadBeutlDependencyContext()
    {
        var assembly = Assembly.LoadFile(Path.Combine(AppContext.BaseDirectory, "Beutl.dll"));
        return DependencyContextLoader.Default.Load(assembly)
            ?? throw new InvalidOperationException();
    }

    private static FrozenSet<string> ReadBeutlLibraries()
    {
        using Stream stream = typeof(CoreLibraries).Assembly.GetManifestResourceStream("BeutlLibraries.txt")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd()
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    private static FrozenDictionary<string, string[]> RuntimeDepsMap => s_runtimeMap.Value;

    private static FrozenDictionary<string, string[]> PackageDepsMap => s_pkgMap.Value;

    public static bool IncludedInRuntimeDependencies(string name, Version? version)
    {
        if (RuntimeDepsMap.TryGetValue(name, out string[]? versions))
        {
            if (version == null)
                return true;

            foreach (string v in versions)
            {
                if (Version.TryParse(v, out Version? installedVersion))
                {
                    return installedVersion >= version;
                }
            }
        }

        return false;
    }

    // A Beutl library counts as included at any version, so the installer never downloads one.
    public static bool IncludedInPackageDependencies(string name, NuGetVersion version)
    {
        if (IsBeutlLibrary(name))
            return true;

        if (PackageDepsMap.TryGetValue(name, out string[]? installedVersions))
        {
            foreach (string v in installedVersions)
            {
                var installedVersion = new NuGetVersion(v);
                return installedVersion >= version;
            }
        }

        return false;
    }

    public static bool IncludedInPackageDependencies(string name, VersionRange versionRange)
    {
        if (IsBeutlLibrary(name))
            return true;

        if (PackageDepsMap.TryGetValue(name, out string[]? installedVersions))
        {
            foreach (string v in installedVersions)
            {
                var version = new NuGetVersion(v);

                return versionRange.Satisfies(version);
            }
        }

        return false;
    }
}
