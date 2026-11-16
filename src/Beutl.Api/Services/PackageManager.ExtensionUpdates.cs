using Beutl.Configuration;
using Microsoft.Extensions.Logging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.Api.Services;

public sealed partial class PackageManager
{
    internal async Task<(int Staged, IReadOnlyList<PackageUpdate> Remaining)> StageExtensionUpdatesAsync(
        NuGetVersion host, bool appUpdate, CancellationToken cancellationToken, IReadOnlyList<PackageIdentity>? packages = null)
    {
        UpdateConfig policy = GlobalConfiguration.Instance.UpdateConfig;
        if (!policy.ShouldUpdateExtensions(appUpdate)) return (0, []);

        using var lifetime = apiApplication.CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetime.Token;
        if (packages == null)
        {
            using var lease = await apiApplication.Lock.LockAsync(token);
            packages = [.. installedPackageRepository.GetLocalPackages()];
        }
        var checkedPackages = packages.ToLookup(p => p.Id, StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<PackageUpdate> available = await CheckUpdatesForHost(host, token, packages).ConfigureAwait(false);
        int staged = 0;
        var remaining = new List<PackageUpdate>();
        var queue = apiApplication.GetResource<PackageChangesQueue>();
        foreach (PackageUpdate update in available)
        {
            token.ThrowIfCancellationRequested();
            string name = update.Package.Name;
            if (update.Package.Tags.Value.GetPackageKind() == PackageKind.Extension
                && checkedPackages[name].MaxBy(p => p.Version) is { } installed
                && NuGetVersion.Parse(update.NewVersion.Version.Value) > installed.Version)
            {
                try
                {
                    bool IsCurrent() => policy.ShouldUpdateExtensions(appUpdate) && !queue.HasPendingChange(name)
                        && installed.Equals(installedPackageRepository.GetLocalPackages(name).MaxBy(p => p.Version));
                    var result = await apiApplication.GetResource<PackageInstaller>()
                        .StageExtensionUpdateAsync(installed, update.NewVersion, host, appUpdate, token, IsCurrent).ConfigureAwait(false);
                    if (result == ExtensionUpdateStageResult.Ready) staged++;
                    if (result != ExtensionUpdateStageResult.RequiresLicenseAcceptance) continue;
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Could not prepare the update of {Package} for Beutl {Host}.", name, host);
                }
            }
            remaining.Add(update);
        }
        return (policy.ShouldUpdateExtensions(appUpdate) ? staged : 0, remaining);
    }
}
