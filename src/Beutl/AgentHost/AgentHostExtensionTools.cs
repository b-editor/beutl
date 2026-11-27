using System.ComponentModel;
using System.Net;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Tools;
using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Logging;
using Beutl.Services;
using Beutl.ViewModels.ExtensionsPages;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using Refit;

namespace Beutl.AgentHost;

[McpServerToolType]
internal sealed class AgentHostExtensionTools(EditorService editor, ProjectService projects) : ToolBase
{
    private static readonly ILogger s_logger = Log.CreateLogger<AgentHostExtensionTools>();

    public sealed record SearchItem(string PackageId, string? DisplayName, string Publisher, string? Description, bool Paid, bool Owned);
    public sealed record SearchResponse(IReadOnlyList<SearchItem> Extensions);
    public sealed record InstalledItem(string PackageId, string? InstalledVersion, string? LoadedVersion,
        string? PendingInstallVersion, bool PendingUninstall, bool RestartRequired);
    public sealed record ListResponse(IReadOnlyList<InstalledItem> Extensions);
    public sealed record InstallResponse(string PackageId, string Version, string Status, bool Loaded, bool RestartRequired);

    [McpServerTool(Name = "search_extensions", ReadOnly = true, OpenWorld = true)]
    [Description("Searches the Beutl Store for extensions. Use a returned packageId with install_extension.")]
    public ValueTask<ToolResult<SearchResponse>> SearchExtensions(
        [Description("Search terms for the extension name or description.")] string query,
        [Description("Number of results to skip.")] int offset = 0,
        [Description("Maximum results to return, from 1 to 30.")] int limit = 20,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(query)) throw Invalid("Provide a search query.", "query");
            if (offset < 0) throw Invalid("Offset must be nonnegative.", "offset");
            if (limit is < 1 or > 30) throw Invalid("Limit must be between 1 and 30.", "limit");

            Package[] matches = await RequireApplication().GetResource<DiscoverService>()
                .Search(query.Trim(), cancellationToken, offset, limit, PackageKindFilter.Extension).ConfigureAwait(false);
            return new SearchResponse([.. matches.Select(package => new SearchItem(package.Name,
                package.DisplayName.Value, package.Owner.Name, package.ShortDescription.Value, IsPaid(package), package.Owned.Value))]);
        });

    [McpServerTool(Name = "list_installed_extensions", ReadOnly = true, OpenWorld = false)]
    [Description("Lists extensions installed in the Beutl profile, the version loaded in this editor, and pending package changes.")]
    public ValueTask<ToolResult<ListResponse>> ListInstalledExtensions(CancellationToken cancellationToken = default)
        => ExecuteAsync(async () =>
        {
            var app = RequireApplication();
            await editor.ExtensionStartupRestrictedMode.WaitAsync(cancellationToken).ConfigureAwait(false);
            using var lease = await app.Lock.LockAsync(cancellationToken).ConfigureAwait(false);
            var installed = app.GetResource<InstalledPackageRepository>().GetProfilePackages()
                .Where(package => PackageManager.ReadInstalledPackage(package)?.Tags.GetPackageKind() == PackageKind.Extension)
                .ToLookup(package => package.Id, StringComparer.OrdinalIgnoreCase);
            var loaded = app.GetResource<PackageManager>().LoadedPackage
                .Where(package => !package.SideLoad)
                .Select(package => new PackageIdentity(package.Name, NuGetVersion.Parse(package.Version)))
                .ToLookup(package => package.Id, StringComparer.OrdinalIgnoreCase);
            var (installs, uninstalls) = app.GetResource<PackageChangesQueue>().GetProfileChanges();
            // The queue is shared with materials and templates, so keep only changes that belong to extensions.
            PackageIdentity[] extensionInstalls = [.. installs.Where(IsExtension)];
            PackageIdentity[] extensionUninstalls = [.. uninstalls.Where(IsExtension)];
            var pending = extensionInstalls.ToLookup(package => package.Id, StringComparer.OrdinalIgnoreCase);
            var pendingRemoval = extensionUninstalls.Select(package => package.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            string[] names = [.. installed.Select(entry => entry.Key).Concat(loaded.Select(entry => entry.Key))
                .Concat(pending.Select(entry => entry.Key)).Concat(pendingRemoval)
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
            return new ListResponse([.. names.Select(name =>
            {
                string? installedVersion = LatestVersion(installed[name]);
                string? loadedVersion = LatestVersion(loaded[name]);
                bool pendingUninstall = pendingRemoval.Contains(name);
                return new InstalledItem(name, installedVersion, loadedVersion, LatestVersion(pending[name]), pendingUninstall,
                    installedVersion != loadedVersion || pending.Contains(name) || pendingUninstall);
            })]);

            bool IsExtension(PackageIdentity package)
            {
                // An installed or loaded extension identifies every queued change for the same package.
                if (installed.Contains(package.Id) || loaded.Contains(package.Id))
                    return true;

                try
                {
                    LocalPackage? local = PackageManager.ReadInstalledPackage(package);
                    if (local is null)
                    {
                        string directory = Helper.ResolveInstalledDirectory(package);
                        if (Directory.Exists(directory))
                        {
                            using var reader = new PackageFolderReader(directory);
                            local = new LocalPackage(reader.NuspecReader);
                        }
                    }
                    if (local is not null)
                        return local.Tags.GetPackageKind() == PackageKind.Extension;

                    // A failed install can leave the archive without extracting it.
                    string archive = Helper.GetNupkgFilePath(package.Id, package.Version.ToString());
                    if (!File.Exists(archive))
                        return false;
                    using FileStream stream = File.OpenRead(archive);
                    return Helper.ReadLocalPackageFromNupkgFile(stream)?.Tags.GetPackageKind() == PackageKind.Extension;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    s_logger.LogWarning(ex, "Could not determine the kind of pending package {PackageId}.", package.Id);
                    return false;
                }
            }
        });

    [McpServerTool(Name = "install_extension", Destructive = false, Idempotent = true, OpenWorld = true)]
    [Description("Installs a Beutl extension in the running editor and its profile. Supply one of packageId, path, or url. Returns installed, already_installed, or restart_required. A conflicting installed version is reported as an error.")]
    public async Task<ToolResult<InstallResponse>> InstallExtension(
        [Description("Beutl Store package ID, as returned by search_extensions.")] string? packageId = null,
        [Description("Exact Store release version. Omit to select the latest compatible release.")] string? version = null,
        [Description("Absolute path to a .nupkg file readable by this editor.")] string? path = null,
        [Description("HTTPS URL serving a .nupkg archive.")] string? url = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateSource(packageId, version, path, url, out NuGetVersion? requestedVersion, out Uri? uri);
            var app = RequireApplication();
            using var lifetime = app.CreateLifetimeLinkedTokenSource(cancellationToken);
            CancellationToken token = lifetime.Token;
            bool restricted = await editor.ExtensionStartupRestrictedMode.WaitAsync(token).ConfigureAwait(false);
            using var lease = await app.Lock.LockAsync(token).ConfigureAwait(false);

            var repository = app.GetResource<InstalledPackageRepository>();
            var manager = app.GetResource<PackageManager>();
            var queue = app.GetResource<PackageChangesQueue>();
            var installer = app.GetResource<PackageInstaller>();
            Release? release = null;
            if (packageId is not null)
            {
                if (requestedVersion is not null && FindExisting(new PackageIdentity(packageId, requestedVersion)) is { } existing)
                    return ToolResult<InstallResponse>.Success(existing);

                release = await ResolveStoreReleaseAsync(app, packageId, version, token).ConfigureAwait(false);
                var identity = new PackageIdentity(release.Package.Name, NuGetVersion.Parse(release.Version.Value));
                if (FindExisting(identity) is { } installed)
                    return ToolResult<InstallResponse>.Success(installed);
                RequireNoPendingChange(queue, identity.Id);
                if (app.AuthenticatedUser.Value is { } user)
                {
                    await user.RefreshAsync(token).ConfigureAwait(false);
                    await app.GetResource<Beutl.Api.Services.LibraryService>().Acquire(release.Package, token).ConfigureAwait(false);
                }
            }

            string directory = Directory.CreateTempSubdirectory("beutl-extension-install-").FullName;
            try
            {
                ExtensionInstallArchive archive = await installer.PrepareExtensionArchiveAsync(directory, release, path, uri, token)
                    .ConfigureAwait(false);
                InstallResponse? alreadyInstalled = null;
                var operation = new PackageOperationHandler(app, editor, projects);
                await installer.TrackInstallOperationAsync(async () =>
                {
                    using var fileChanges = await PackageInstaller.LockExtensionInstallAsync(token).ConfigureAwait(false);
                    alreadyInstalled = FindExisting(archive.Identity);
                    if (alreadyInstalled is not null)
                    {
                        await PackageInstaller.VerifyExtensionArchiveAsync(archive, token).ConfigureAwait(false);
                        return;
                    }
                    RequireNoPendingChange(queue, archive.Identity.Id);
                    await installer.ResolveExtensionInstallAsync(archive, token).ConfigureAwait(false);
                    await operation.ActivateInstalledPackageAsync(archive.Identity, token,
                        loadExtension: !restricted, allowReplacement: false).ConfigureAwait(false);
                }).ConfigureAwait(false);
                return ToolResult<InstallResponse>.Success(alreadyInstalled ?? DescribeInstall(archive.Identity,
                    restricted ? "restart_required" : "installed", loaded: !restricted, restartRequired: restricted));
            }
            finally
            {
                try { Directory.Delete(directory, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    s_logger.LogWarning(ex, "Could not remove extension install staging at {Directory}.", directory);
                }
            }

            InstallResponse? FindExisting(PackageIdentity package)
            {
                PackageIdentity[] installed = repository.GetProfilePackages();
                foreach (PackageIdentity other in installed.Where(item => StringComparer.OrdinalIgnoreCase.Equals(item.Id, package.Id)))
                {
                    if (!package.Equals(other)) throw new PackageVersionConflictException(other);
                }
                LocalPackage[] active = manager.FindLoadedPackage(package.Id);
                foreach (LocalPackage other in active)
                {
                    if (other.SideLoad)
                        throw new ReconcileException(new ToolError("extension_version_conflict",
                            $"'{package.Id}' is already loaded from the sideload directory.", package.Id));
                    var loadedId = new PackageIdentity(other.Name, NuGetVersion.Parse(other.Version));
                    if (!package.Equals(loadedId))
                        throw new PackageVersionConflictException(loadedId);
                }
                // A sideload waiting for its load decision would load beside a profile copy installed now.
                if (manager.GetSideLoadPackages().Any(other =>
                        StringComparer.OrdinalIgnoreCase.Equals(other.Name, package.Id)))
                {
                    throw new ReconcileException(new ToolError("extension_version_conflict",
                        $"'{package.Id}' is already side-loaded in the sideload directory.", package.Id));
                }
                if (!installed.Contains(package) || PackageManager.ReadInstalledPackage(package) is not { } installedPackage)
                    return null;
                if (installedPackage.Tags.GetPackageKind() != PackageKind.Extension)
                    throw Invalid($"'{package.Id}' is not an extension package.", "packageId");
                // A queued uninstall would remove this version at the next launch, so do not report it as settled.
                RequireNoPendingChange(queue, package.Id);
                return DescribeInstall(package, "already_installed", active.Length != 0, active.Length == 0);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ReconcileException ex)
        {
            return new ToolResult<InstallResponse>(null, ex.Error);
        }
        catch (PackageVersionConflictException ex)
        {
            return ToolResult<InstallResponse>.Failure("extension_version_conflict", ex.Message, ex.Installed.Id);
        }
        catch (PackageArchiveConflictException ex)
        {
            return ToolResult<InstallResponse>.Failure("extension_archive_conflict", ex.Message);
        }
        catch (PackageLicenseRequiredException ex)
        {
            return ToolResult<InstallResponse>.Failure("extension_license_required", ex.Message, ex.Package.Id);
        }
        catch (Exception ex) when (ex is PackageTargetMismatchException or PackageApiMismatchException)
        {
            return ToolResult<InstallResponse>.Failure("extension_incompatible", ex.Message, packageId);
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return ToolResult<InstallResponse>.Failure("extension_not_found", "The Store package or release was not found.", packageId);
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Extension installation failed.");
            return ToolResult<InstallResponse>.Failure("extension_install_failed",
                $"The extension could not be installed ({ex.GetType().Name}).", packageId,
                "See the Beutl log for details.");
        }
    }

    private static async Task<Release> ResolveStoreReleaseAsync(BeutlApiApplication app, string packageId, string? version,
        CancellationToken cancellationToken)
    {
        Package package = await app.GetResource<DiscoverService>().GetPackage(packageId, cancellationToken).ConfigureAwait(false);
        if (package.Tags.Value.GetPackageKind() != PackageKind.Extension)
            throw Invalid($"'{packageId}' is not an extension package.", "packageId");
        if (IsPaid(package) && !package.Owned.Value)
            throw new ReconcileException(new ToolError("extension_purchase_required",
                $"'{packageId}' must be purchased in the Beutl Store before installation.", packageId));

        Release? release = version is not null
            ? await package.GetReleaseAsync(version, cancellationToken).ConfigureAwait(false)
            : await package.GetLatestCompatibleReleaseAsync(cancellationToken,
                automaticUpdateHost: NuGetVersion.Parse(BeutlApplication.Version)).ConfigureAwait(false);
        if (release is null || !release.CanInstall())
            throw new PackageTargetMismatchException($"No requested release of '{packageId}' supports Beutl {BeutlApplication.Version}.");
        if (release.AssetId.Value is null)
            throw new ReconcileException(new ToolError("extension_not_found", $"Release {release.Version.Value} has no archive.", packageId));
        return release;
    }

    private static void ValidateSource(string? packageId, string? version, string? path, string? url,
        out NuGetVersion? parsedVersion, out Uri? uri)
    {
        if (new[] { packageId, path, url }.Count(value => value is not null) != 1)
            throw Invalid("Supply exactly one of packageId, path or url.");
        if (packageId is not null && !PackageIdValidator.IsValidPackageId(packageId))
            throw Invalid("Provide a valid NuGet package ID.", "packageId");

        parsedVersion = null;
        if (version is not null && (packageId is null || !NuGetVersion.TryParse(version, out parsedVersion)))
            throw Invalid("Version must be a NuGet version and is available only with packageId.", "version");

        if (path is not null && (!Path.IsPathFullyQualified(path) || !Path.GetExtension(path).Equals(".nupkg", StringComparison.OrdinalIgnoreCase)))
            throw Invalid("Provide an absolute path to a .nupkg file.", "path");
        uri = null;
        if (url is not null && (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0))
            throw Invalid("Provide an HTTPS URL without embedded credentials.", "url");
    }

    private static void RequireNoPendingChange(PackageChangesQueue queue, string packageId)
    {
        var (installs, uninstalls) = queue.GetProfileChanges();
        if (installs.Concat(uninstalls).Any(package => StringComparer.OrdinalIgnoreCase.Equals(package.Id, packageId)))
            throw new ReconcileException(new ToolError("extension_change_pending",
                $"There is already a pending change for '{packageId}'. Manage it from Beutl's Extensions window.", packageId));
    }

    private static InstallResponse DescribeInstall(PackageIdentity package, string status, bool loaded, bool restartRequired)
        => new(package.Id, package.Version.ToString(), status, loaded, restartRequired);

    private static string? LatestVersion(IEnumerable<PackageIdentity> packages)
        => packages.MaxBy(package => package.Version)?.Version?.ToString();

    private static bool IsPaid(Package package) => package.Paid.Value || package.Price.Value is > 0;

    private BeutlApiApplication RequireApplication()
        => editor.ApiApplication ?? throw new ReconcileException(new ToolError(ErrorCode.LiveUnavailable,
            "Extension management is unavailable in this Beutl instance."));

    private static ReconcileException Invalid(string message, string? target = null)
        => new(new ToolError(ErrorCode.ValidationRejected, message, target));
}
