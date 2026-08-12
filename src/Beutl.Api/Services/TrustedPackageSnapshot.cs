using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.Api.Services;

/// <summary>
/// Immutable evidence that connects a verified Marketplace artifact to the exact
/// managed bytes loaded for its feature declarations. No mutable package path is
/// consulted after this snapshot is created.
/// </summary>
internal sealed class TrustedPackageSnapshot
{
    // Exact attribution is optional. Bound the temporary artifact and retained
    // managed snapshot so malformed or oversized package content falls back to
    // generic telemetry rather than consuming unbounded process memory.
    internal const long MaxArtifactBytes = 64L * 1024 * 1024;
    internal const long MaxCapturedAssemblyBytes = 64L * 1024 * 1024;

    private readonly Dictionary<string, byte[]> _assemblyBytes;
    private readonly HashSet<Assembly> _loadedAssemblies = [];
    private readonly object _loadedAssembliesGate = new();

    private TrustedPackageSnapshot(
        string packageRoot,
        string canonicalMarketplacePackageId,
        AnalyticsFeatureManifest manifest,
        Dictionary<string, byte[]> assemblyBytes)
    {
        PackageRoot = packageRoot;
        CanonicalMarketplacePackageId = canonicalMarketplacePackageId;
        Manifest = manifest;
        _assemblyBytes = assemblyBytes;
    }

    internal string PackageRoot { get; }

    internal string CanonicalMarketplacePackageId { get; }

    internal AnalyticsFeatureManifest Manifest { get; }

    internal static TrustedPackageSnapshot? TryCreate(
        InstalledPackageRepository repository,
        LocalPackage package,
        PackageLoadLayout layout)
    {
        if (package.SideLoad
            || string.IsNullOrWhiteSpace(package.InstalledPath)
            || !NuGetVersion.TryParse(package.Version, out NuGetVersion? version))
        {
            return null;
        }

        var identity = new PackageIdentity(package.Name, version);
        if (!repository.TryGetVerifiedAnalyticsProvenance(
                identity,
                out PackageAnalyticsProvenance? provenance)
            || PackageAnalyticsProvenance.CanonicalizePackageId(identity.Id)
                != provenance.CanonicalMarketplacePackageId
            || provenance.CanonicalMarketplacePackageId is not { } canonicalMarketplacePackageId)
        {
            return null;
        }

        string packageRoot = Path.GetFullPath(package.InstalledPath);
        if (!IsInsidePackage(layout.MainDirectory, packageRoot))
        {
            return null;
        }

        string artifactPath = PackageAnalyticsProvenance.GetArtifactPath(packageRoot);
        if (TryReadFile(artifactPath, MaxArtifactBytes) is not { } artifactBytes)
        {
            return null;
        }

        string artifactSha256 = Convert.ToHexString(SHA256.HashData(artifactBytes));
        if (!StringComparer.OrdinalIgnoreCase.Equals(artifactSha256, provenance.PackageSha256))
        {
            return null;
        }

        using var artifactStream = new MemoryStream(artifactBytes, writable: false);
        using var archive = new ZipArchive(artifactStream, ZipArchiveMode.Read, leaveOpen: false);
        // Native libraries must be loaded from an OS-visible path. Since an
        // AssemblyLoadContext cannot load them from our immutable managed-byte
        // snapshot, do not grant exact attribution to a package that carries a
        // mutable in-package native runtime asset.
        if (archive.Entries.Any(IsNativeRuntimeAsset))
        {
            return null;
        }

        if (AnalyticsFeatureManifest.TryLoadFromArchive(
                archive,
                provenance.ApprovedManifestSha256) is not { } manifest
            || AnalyticsFeatureManifest.TryLoadFromInstalledDirectory(
                packageRoot,
                provenance.ApprovedManifestSha256) is null)
        {
            return null;
        }

        var assemblyBytes = new Dictionary<string, byte[]>(GetPathComparer());
        long capturedAssemblyBytes = 0;
        foreach (string assemblyPath in layout.AssemblyPaths)
        {
            string fullPath = Path.GetFullPath(assemblyPath);
            if (!IsInsidePackage(fullPath, packageRoot)
                || !TryFindExactArchiveEntry(archive, packageRoot, fullPath, out ZipArchiveEntry? entry)
                || entry is null)
            {
                return null;
            }

            if (TryReadEntry(entry, ref capturedAssemblyBytes) is not { } archivedBytes
                || !FileMatchesBytes(fullPath, archivedBytes)
                || !assemblyBytes.TryAdd(fullPath, archivedBytes))
            {
                return null;
            }
        }

        // The package resolver can later request a satellite or another managed
        // dependency below the package root. Snapshot every in-package DLL so it
        // cannot reopen a mutable path after the initial verified load.
        foreach (ZipArchiveEntry entry in archive.Entries.Where(static candidate =>
                     candidate.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            if (!TryGetArchivePackagePath(packageRoot, entry.FullName, out string fullPath))
            {
                return null;
            }

            if (assemblyBytes.ContainsKey(fullPath))
            {
                continue;
            }

            if (TryReadEntry(entry, ref capturedAssemblyBytes) is not { } archivedBytes
                || !FileMatchesBytes(fullPath, archivedBytes)
                || !assemblyBytes.TryAdd(fullPath, archivedBytes))
            {
                return null;
            }
        }

        return new TrustedPackageSnapshot(
            packageRoot,
            canonicalMarketplacePackageId,
            manifest,
            assemblyBytes);
    }

    internal bool TryGetAssemblyBytes(string assemblyPath, out byte[] bytes)
    {
        return _assemblyBytes.TryGetValue(Path.GetFullPath(assemblyPath), out bytes!);
    }

    internal bool IsPathInsidePackage(string path)
    {
        return IsInsidePackage(Path.GetFullPath(path), PackageRoot);
    }

    internal void RegisterLoadedAssembly(Assembly assembly)
    {
        lock (_loadedAssembliesGate)
        {
            _loadedAssemblies.Add(assembly);
        }
    }

    internal bool IsVerifiedAssembly(Assembly assembly)
    {
        lock (_loadedAssembliesGate)
        {
            return _loadedAssemblies.Contains(assembly);
        }
    }

    private static bool TryFindExactArchiveEntry(
        ZipArchive archive,
        string packageRoot,
        string assemblyPath,
        out ZipArchiveEntry? entry)
    {
        string relativePath = Path.GetRelativePath(packageRoot, assemblyPath)
            .Replace(Path.DirectorySeparatorChar, '/');
        if (relativePath.StartsWith("../", StringComparison.Ordinal)
            || Path.IsPathRooted(relativePath))
        {
            entry = null;
            return false;
        }

        ZipArchiveEntry[] entries = archive.Entries
            .Where(candidate => string.Equals(
                candidate.FullName,
                relativePath,
                StringComparison.Ordinal))
            .ToArray();
        entry = entries.Length == 1 ? entries[0] : null;
        return entry is not null;
    }

    private static bool IsNativeRuntimeAsset(ZipArchiveEntry entry)
    {
        return !entry.FullName.EndsWith("/", StringComparison.Ordinal)
            && entry.FullName.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase)
            && entry.FullName.Contains("/native/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetArchivePackagePath(
        string packageRoot,
        string entryPath,
        out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(entryPath)
            || entryPath.Contains('\\')
            || Path.IsPathRooted(entryPath))
        {
            return false;
        }

        string candidate = Path.GetFullPath(Path.Combine(
            packageRoot,
            entryPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsInsidePackage(candidate, packageRoot))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }

    private static byte[]? TryReadFile(string path, long maximumLength)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            return stream.Length > maximumLength ? null : ReadExactly(stream, stream.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    private static byte[]? TryReadEntry(ZipArchiveEntry entry, ref long capturedAssemblyBytes)
    {
        if (!TryReserveCapturedAssemblyBytes(ref capturedAssemblyBytes, entry.Length))
        {
            return null;
        }

        try
        {
            using Stream stream = entry.Open();
            return ReadExactly(stream, entry.Length);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static bool TryReserveCapturedAssemblyBytes(ref long capturedAssemblyBytes, long nextLength)
    {
        if (nextLength < 0 || nextLength > MaxCapturedAssemblyBytes - capturedAssemblyBytes)
        {
            return false;
        }

        capturedAssemblyBytes += nextLength;
        return true;
    }

    private static bool FileMatchesBytes(string path, ReadOnlySpan<byte> expected)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            if (stream.Length != expected.Length)
            {
                return false;
            }

            Span<byte> buffer = stackalloc byte[8192];
            int offset = 0;
            while (offset < expected.Length)
            {
                int read = stream.Read(buffer[..Math.Min(buffer.Length, expected.Length - offset)]);
                if (read == 0 || !buffer[..read].SequenceEqual(expected.Slice(offset, read)))
                {
                    return false;
                }

                offset += read;
            }

            return stream.ReadByte() == -1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static byte[] ReadExactly(Stream stream, long length)
    {
        if (length < 0 || length > Array.MaxLength)
        {
            throw new InvalidDataException("Package assembly is too large to verify.");
        }

        byte[] bytes = GC.AllocateUninitializedArray<byte>((int)length);
        int offset = 0;
        while (offset < bytes.Length)
        {
            int read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read == 0)
            {
                throw new InvalidDataException("Package assembly ended before its declared length.");
            }

            offset += read;
        }

        if (stream.ReadByte() != -1)
        {
            throw new InvalidDataException("Package assembly exceeded its declared length.");
        }

        return bytes;
    }

    private static bool IsInsidePackage(string path, string packageRoot)
    {
        string rootWithSeparator = Path.EndsInDirectorySeparator(packageRoot)
            ? packageRoot
            : packageRoot + Path.DirectorySeparatorChar;
        return path.StartsWith(rootWithSeparator, GetPathComparison());
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }
}
