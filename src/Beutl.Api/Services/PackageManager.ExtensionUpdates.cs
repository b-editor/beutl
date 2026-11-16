using Beutl.Configuration;
using Microsoft.Extensions.Logging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.Api.Services;

public sealed partial class PackageManager
{
    internal async Task<(int Staged, IReadOnlyList<PackageUpdate> Remaining)> StageExtensionUpdatesAsync(
        NuGetVersion host, bool appUpdate, CancellationToken cancellationToken)
    {
        UpdateConfig policy = GlobalConfiguration.Instance.UpdateConfig;
        if (!policy.ShouldUpdateExtensions(appUpdate)) return (0, []);

        using var lifetime = apiApplication.CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetime.Token;
        using var lease = await apiApplication.Lock.LockAsync(token);
        IReadOnlyList<PackageUpdate> available = await CheckUpdatesForHost(host, token).ConfigureAwait(false);
        int staged = 0;
        var remaining = new List<PackageUpdate>();
        var queue = apiApplication.GetResource<PackageChangesQueue>();
        foreach (PackageUpdate update in available)
        {
            token.ThrowIfCancellationRequested();
            string name = update.Package.Name;
            if (update.Package.Tags.Value.GetPackageKind() == PackageKind.Extension
                && policy.ShouldUpdateExtensions(appUpdate) && !queue.HasPendingChange(name)
                && installedPackageRepository.GetLocalPackages(name).MaxBy(p => p.Version) is { } installed
                && NuGetVersion.Parse(update.NewVersion.Version.Value) > installed.Version)
            {
                try
                {
                    await apiApplication.GetResource<PackageInstaller>()
                        .StageExtensionUpdateAsync(installed, update.NewVersion, host, appUpdate, token).ConfigureAwait(false);
                    staged++;
                    continue;
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
