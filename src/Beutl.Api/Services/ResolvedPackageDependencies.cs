using System.Text.Json;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Resolver;
using NuGet.Versioning;

namespace Beutl.Api.Services;

internal static class ResolvedPackageDependencies
{
    internal const string FileName = ".beutl-dependencies.json";

    private sealed record StoredPackage(string Id, string Version);
    private sealed record Snapshot(string Framework, StoredPackage Root, StoredPackage[] Packages);

    public static void Save(string directory, NuGetFramework framework, IEnumerable<PackageIdentity> packages)
    {
        using var reader = new PackageFolderReader(directory);
        PackageIdentity root = reader.GetIdentity();
        var snapshot = new Snapshot(
            framework.GetShortFolderName(),
            new StoredPackage(root.Id, root.Version.ToNormalizedString()),
            packages.Append(root).Distinct(PackageIdentityComparer.Default)
                .Select(package => new StoredPackage(package.Id, package.Version.ToNormalizedString())).ToArray());
        JsonSerializer.SerializeToNode(snapshot)!.JsonSave(Path.Combine(directory, FileName));
    }

    public static IReadOnlyList<PackageIdentity> Load(PackageFolderReader reader, NuGetFramework framework)
    {
        PackageIdentity root = reader.GetIdentity();
        string directory = Path.GetDirectoryName(reader.GetNuspecFile())!;
        string path = Path.Combine(directory, FileName);
        if (File.Exists(path))
        {
            Snapshot snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(path))
                ?? throw new InvalidDataException("The package dependency snapshot is empty.");
            if (!PackageIdentityComparer.Default.Equals(ReadIdentity(snapshot.Root), root))
                throw new InvalidDataException("The dependency snapshot belongs to another package.");
            if (snapshot.Framework == framework.GetShortFolderName())
            {
                PackageIdentity[] packages = snapshot.Packages.Select(ReadIdentity).ToArray();
                if (!packages.Contains(root, PackageIdentityComparer.Default)
                    || packages.Select(package => package.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != packages.Length)
                    throw new InvalidDataException("The package dependency snapshot is inconsistent.");
                return packages;
            }
        }

        // Older installs have no snapshot. Resolve their complete installed graph,
        // rather than selecting each edge's minimum independently in traversal order.
        var available = new Dictionary<PackageIdentity, SourcePackageDependencyInfo>(PackageIdentityComparer.Default);
        var pending = new Queue<SourcePackageDependencyInfo>();
        SourcePackageDependencyInfo rootInfo = ReadInfo(reader, framework);
        available.Add(rootInfo, rootInfo);
        pending.Enqueue(rootInfo);
        string[] directories = Directory.Exists(Helper.InstallPath) ? Directory.GetDirectories(Helper.InstallPath) : [];
        while (pending.TryDequeue(out SourcePackageDependencyInfo? current))
        {
            foreach (PackageDependency dependency in current.Dependencies)
            {
                string prefix = dependency.Id + ".";
                foreach (string candidateDirectory in directories)
                {
                    string name = Path.GetFileName(candidateDirectory);
                    if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        || !NuGetVersion.TryParse(name[prefix.Length..], out NuGetVersion? version)
                        || !dependency.VersionRange.Satisfies(version))
                        continue;

                    var identity = new PackageIdentity(dependency.Id, version);
                    if (available.ContainsKey(identity)
                        || StringComparer.OrdinalIgnoreCase.Equals(identity.Id, root.Id))
                        continue;
                    using var candidateReader = new PackageFolderReader(candidateDirectory);
                    SourcePackageDependencyInfo candidate = ReadInfo(candidateReader, framework);
                    if (!PackageIdentityComparer.Default.Equals(candidate, identity))
                        throw new InvalidDataException("An installed package does not match its directory.");
                    available.Add(candidate, candidate);
                    pending.Enqueue(candidate);
                }
            }
        }

        var context = new PackageResolverContext(DependencyBehavior.Lowest, [root.Id], [], [],
            CoreLibraries.GetPreferredVersions(), available.Values, [], NullLogger.Instance);
        return new PackageResolver().Resolve(context, CancellationToken.None).ToArray();
    }

    private static PackageIdentity ReadIdentity(StoredPackage package)
    {
        if (package is null || string.IsNullOrEmpty(package.Id) || !PackageIdValidator.IsValidPackageId(package.Id)
            || !NuGetVersion.TryParse(package.Version, out NuGetVersion? version))
            throw new InvalidDataException("The dependency snapshot contains an invalid package identity.");
        return new PackageIdentity(package.Id, version);
    }

    private static SourcePackageDependencyInfo ReadInfo(PackageFolderReader reader, NuGetFramework framework)
    {
        PackageDependencyGroup[] groups = reader.GetPackageDependencies().ToArray();
        NuGetFramework? nearest = Helper.FrameworkReducer.GetNearest(framework, groups.Select(group => group.TargetFramework));
        return new SourcePackageDependencyInfo(reader.GetIdentity(), groups
            .Where(group => group.TargetFramework == nearest).SelectMany(group => group.Packages)
            .Where(dependency => !CoreLibraries.IncludedInPackageDependencies(dependency.Id, dependency.VersionRange)),
            listed: true, source: null, downloadUri: null, packageHash: null);
    }
}
