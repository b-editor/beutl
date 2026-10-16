using System.Text;
using NuGet.Versioning;

namespace Beutl.Api.Services;

// Lets a package built for another Beutl version load only if the host has every API it references.
internal sealed class PackageCompatibility(PackageApiCheckCache cache)
{
    public void EnsureCompatible(
        LocalPackage package,
        IReadOnlyCollection<string> assemblies,
        Func<IReadOnlyDictionary<string, string?>> hostAssemblies)
    {
        if (NuGetVersion.TryParse(package.TargetVersion, out NuGetVersion? target)
            && NuGetVersion.TryParse(BeutlApplication.Version, out NuGetVersion? host)
            && target == host)
        {
            return;
        }

        IReadOnlyDictionary<string, string?> hostMap = hostAssemblies();
        string key = PackageApiCheckCache.CreateKey(package.Version, assemblies, hostMap, BeutlApplication.Version);
        if (cache.HasPassed(package.Name, key))
        {
            return;
        }

        IReadOnlyList<PackageApiProblem> problems = PackageApiChecker.Check(assemblies, hostMap);
        if (problems.Count > 0)
        {
            throw new PackageApiMismatchException(package, problems);
        }

        cache.SavePass(package.Name, key);
    }

    // The host's assemblies by simple name, without the ones the package brings, since its own copies win.
    public static Dictionary<string, string?> HostAssemblies(IEnumerable<string> packageAssemblyNames)
    {
        var host = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        string trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "";
        foreach (string path in trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            host.TryAdd(Path.GetFileNameWithoutExtension(path), path);
        }

        foreach (string name in packageAssemblyNames)
        {
            host.Remove(name);
        }

        return host;
    }
}

internal sealed class PackageApiMismatchException(LocalPackage package, IReadOnlyList<PackageApiProblem> problems)
    : Exception(Describe(package, problems))
{
    public LocalPackage Package { get; } = package;

    public IReadOnlyList<PackageApiProblem> Problems { get; } = problems;

    private static string Describe(LocalPackage package, IReadOnlyList<PackageApiProblem> problems)
    {
        var text = new StringBuilder(
            $"{package.Name} {package.Version} was built for Beutl {package.TargetVersion ?? "an unknown version"} "
            + $"and uses APIs that Beutl {BeutlApplication.Version} does not have:");
        foreach (PackageApiProblem problem in problems)
        {
            text.Append($"{Environment.NewLine}  {problem.Kind}: {problem.Api} ({problem.Assembly})");
        }

        return text.ToString();
    }
}
