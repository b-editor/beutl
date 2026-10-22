using System.Diagnostics;
using Beutl.Extensibility;
using Microsoft.Extensions.Logging;
using Telemetry = Beutl.Api.Services.PackageManagementActivitySource;

namespace Beutl.Api.Services;

public sealed partial class PackageManager
{
    public ValueTask<bool> Unload(LocalPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        TaskCompletionSource<bool>? completion = null;
        Task<bool> operation;
        lock (_packageLifecycleGate)
        {
            if (_unloadOperations.TryGetValue(package.LocalId, out operation!))
                return new ValueTask<bool>(operation);

            if (_quarantinedPackages.Contains(package.LocalId))
                return new ValueTask<bool>(false);

            if (_loadingPackages.Contains(package.LocalId))
                return new ValueTask<bool>(false);

            completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            operation = completion.Task;
            _unloadOperations.Add(package.LocalId, operation);
        }

        _ = RunUnloadAsync(package, completion);
        return new ValueTask<bool>(operation);
    }

    private async Task RunUnloadAsync(
        LocalPackage package,
        TaskCompletionSource<bool> completion)
    {
        try
        {
            completion.TrySetResult(await UnloadOnceAsync(package).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
        finally
        {
            lock (_packageLifecycleGate)
            {
                if (!_quarantinedPackages.Contains(package.LocalId))
                {
                    _unloadOperations.Remove(package.LocalId);
                }
            }
        }
    }

    private async Task<bool> UnloadOnceAsync(LocalPackage package)
    {
        using (Activity? activity = Telemetry.ActivitySource.StartActivity("Unload"))
        {
            WeakReference? weakReference = await UnloadCoreAsync(activity, package);
            if (weakReference is null)
            {
                return false;
            }

            for (int i = 0; weakReference.IsAlive && (i < 10); i++)
            {
                GC.Collect();
                GC.WaitForFullGCComplete(-1);
                GC.WaitForPendingFinalizers();
                await Task.Delay(100).ConfigureAwait(false);
            }

            return !weakReference.IsAlive;
        }
    }

    private async ValueTask<WeakReference?> UnloadCoreAsync(
        Activity? activity,
        LocalPackage package)
    {
        activity?.SetTag("PackageName", package.Name);

        if (!_loadedPackages.TryGetValue(package.LocalId, out LoadedPackageInfo? info))
        {
            _logger.LogWarning("Package {PackageName} is not loaded.", package.Name);
            return null;
        }

        ExtensionRemoval removal = RemovePackageRegistrations(
            package,
            out List<Exception>? retirementFailures);
        IReadOnlyList<Extension> extensions = removal.Extensions;
        retirementFailures = RetireExtensions(extensions, retirementFailures);

        try
        {
            // Drain every extension in the package before invoking any extension-level unload.
            // Packages commonly share static resources across several extension entry points.
            await removal.DrainAsync();
        }
        catch (Exception ex)
        {
            (retirementFailures ??= []).Add(ex);
            _logger.LogError(
                ex,
                "Package {PackageName} registrations failed to drain; the load context remains quarantined.",
                package.Name);
        }

        if (retirementFailures is not null)
        {
            lock (_packageLifecycleGate)
            {
                _quarantinedPackages.Add(package.LocalId);
            }

            activity?.SetStatus(ActivityStatusCode.Error, "Extension retirement failed");
            return null;
        }

        UnloadExtensions(extensions);

        _loadedPackages.TryRemove(package.LocalId, out _);

        if (info.LoadContext is { } loadContext)
        {
            TryUnloadLoadContext(package, loadContext);
        }

        // https://learn.microsoft.com/ja-jp/dotnet/standard/assembly/unloadability#use-a-custom-collectible-assemblyloadcontext
        return new WeakReference(info.LoadContext, trackResurrection: true);
    }

    private ExtensionRemoval RemovePackageRegistrations(
        LocalPackage package,
        out List<Exception>? retirementFailures)
    {
        retirementFailures = null;
        try
        {
            return extensionRegistry.RemoveExtensions(package.LocalId);
        }
        catch (ExtensionRemovalNotificationException ex)
        {
            retirementFailures = [ex];
            return ex.Removal;
        }
    }

    private List<Exception>? RetireExtensions(
        IReadOnlyList<Extension> extensions,
        List<Exception>? retirementFailures)
    {
        foreach (Extension ext in extensions)
        {
            if (ext is ViewExtension viewExtension)
            {
                try
                {
                    // Commands are a discoverability surface too. Retire them before waiting so
                    // no new package-owned callback can start while the package is draining.
                    commandManager.Unregister(viewExtension);
                }
                catch (Exception ex)
                {
                    (retirementFailures ??= []).Add(ex);
                    _logger.LogError(
                        ex,
                        "Failed to unregister commands for extension {ExtensionName}.",
                        ext.GetType().Name);
                }
            }

            try
            {
                // Configuration notifications can call package code and therefore belong to the
                // synchronous retirement phase, not post-unload cleanup.
                CleanupExtensionSettings(ext);
            }
            catch (Exception ex)
            {
                (retirementFailures ??= []).Add(ex);
                _logger.LogError(
                    ex,
                    "Failed to detach settings for extension {ExtensionName}.",
                    ext.GetType().Name);
            }
        }

        return retirementFailures;
    }

    private void UnloadExtensions(IReadOnlyList<Extension> extensions)
    {
        foreach (Extension ext in extensions)
        {
            try
            {
                ext.Unload();
                _logger.LogInformation("Extension {ExtensionName} unloaded.", ext.GetType().Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to unload extension {ExtensionName}.", ext.GetType().Name);
            }
        }
    }
}
