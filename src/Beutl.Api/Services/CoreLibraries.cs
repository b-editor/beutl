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

    public static IEnumerable<PackageIdentity> GetPreferredVersions()
    {
        s_preferredVersions ??= CollectPackageDependencies()
            .Select(x => new PackageIdentity(x.Name, NuGetVersion.Parse(x.Version)))
            .ToList();

        return s_preferredVersions;
    }

    public static IEnumerable<Dependency> CollectPackageDependencies()
    {
        var assembly = Assembly.LoadFile(Path.Combine(AppContext.BaseDirectory, "Beutl.dll"));
        DependencyContext? depsContext = DependencyContextLoader.Default.Load(assembly)
            ?? throw new InvalidOperationException();

        var library = new HashSet<Dependency>();

        foreach (RuntimeLibrary lib in depsContext.RuntimeLibraries)
        {
            library.Add(new(lib.Name, lib.Version));
        }

#if DEBUG
        library.Add(new("Beutl.Sdk", "1.0.0-preview.5"));
#else
        library.Add(new("Beutl.Sdk", GitVersionInformation.NuGetVersionV2));
#endif

        return library;
    }

    public static IEnumerable<Dependency> CollectRuntimeDependencies()
    {
        var assembly = Assembly.LoadFile(Path.Combine(AppContext.BaseDirectory, "Beutl.dll"));
        DependencyContext? depsContext = DependencyContextLoader.Default.Load(assembly)
            ?? throw new InvalidOperationException();

        var library = new HashSet<Dependency>();

        foreach (RuntimeLibrary lib in depsContext.RuntimeLibraries)
        {
            foreach (RuntimeAssetGroup group in lib.RuntimeAssemblyGroups)
            {
                foreach (RuntimeFile item in group.RuntimeFiles)
                {
                    string fileName = Path.GetFileNameWithoutExtension(item.Path);
                    string? version = item.AssemblyVersion;

                    if (version == null)
                    {
                        switch (fileName)
                        {
                            case "Beutl":
                            case "Beutl.Api":
                            case "Beutl.Api.Generated":
                            case "Beutl.Configuration":
                            case "Beutl.Controls":
                            case "Beutl.Core":
                            case "Beutl.Embedding.FFmpeg":
                            case "Beutl.Embedding.MediaFoundation" when OperatingSystem.IsWindows():
                            case "Beutl.Engine":
                            case "Beutl.Extensibility":
                            case "Beutl.Language":
                            case "Beutl.Operators":
                            case "Beutl.ProjectSystem":
                            case "Beutl.Threading":
                            case "Beutl.Utilities":
                            case "Beutl.WaitingDialog":
                            case "Beutl.PackageTools":
                            case "Beutl.ExceptionHandler":
                                version = GitVersionInformation.AssemblySemVer;
                                break;
                            default:
                                break;
                        }
                    }

                    if (version != null)
                        library.Add(new Dependency(fileName, version));
                }
            }
        }

        return library;
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

    public static bool IncludedInPackageDependencies(string name, NuGetVersion version)
    {
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
