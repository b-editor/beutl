using System.Diagnostics;
using Beutl.Extensibility;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using Telemetry = Beutl.Api.Services.PackageManagemantActivitySource;

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
            PackageUnloadResult? result = await UnloadCoreAsync(activity, package);
            if (result is null)
            {
                return false;
            }

            WeakReference weakReference = result.LoadContextReference;
            string[] assemblyNames = result.AssemblyNames;

            for (int i = 0; weakReference.IsAlive && (i < 10); i++)
            {
                GC.Collect();
                GC.WaitForFullGCComplete(-1);
                GC.WaitForPendingFinalizers();
                await Task.Delay(100).ConfigureAwait(false);
            }

            bool unloaded = !weakReference.IsAlive;
            if (!unloaded && unloadDiagnostics is { } diagnostics && assemblyNames.Length > 0)
            {
                activity?.AddEvent(new ActivityEvent("Prompting for unload diagnostics"));
                // Ask before snapshotting: the ClrMD self-snapshot is heavy, so the developer decides whether it runs.
                PromptCaptureUnloadDiagnostics(diagnostics, package.Name, assemblyNames);
            }

            return unloaded;
        }
    }

    private async ValueTask<PackageUnloadResult?> UnloadCoreAsync(
        Activity? activity,
        LocalPackage package)
    {
        string[] assemblyNames = [];
        activity?.SetTag("PackageName", package.Name);

        if (package.LocalId == LocalPackage.Reserved0)
        {
            _logger.LogWarning("Cannot unload built-in extensions.");
            return null;
        }

        if (!_loadedPackages.TryGetValue(package.LocalId, out LoadedPackageInfo? info))
        {
            _logger.LogWarning("Package {PackageName} is not loaded.", package.Name);
            return null;
        }

        // Only the registries below have explicit operation leases. Other
        // extension families create long-lived editor/output/window objects,
        // so their package files are updated safely on the next restart.
        ExtensionRemoval? removal = RemovePackageRegistrations(
            package,
            out bool requiresRestart,
            out List<Exception>? retirementFailures);
        if (requiresRestart)
        {
            _logger.LogInformation(
                "Package {PackageName} contains extensions that require restart for safe unload.",
                package.Name);
            return null;
        }

        if (removal is null)
        {
            throw new InvalidOperationException(
                $"The extension registry did not remove package {package.Name}.");
        }

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
            assemblyNames = CaptureAssemblyNames(package, loadContext);
            TryUnloadLoadContext(package, loadContext);
        }

        // https://learn.microsoft.com/ja-jp/dotnet/standard/assembly/unloadability#use-a-custom-collectible-assemblyloadcontext
        return new PackageUnloadResult(
            new WeakReference(info.LoadContext, trackResurrection: true),
            assemblyNames);
    }

    private ExtensionRemoval? RemovePackageRegistrations(
        LocalPackage package,
        out bool requiresRestart,
        out List<Exception>? retirementFailures)
    {
        ExtensionRemoval? removal = null;
        var restartRequired = false;
        retirementFailures = null;
        try
        {
            extensionRegistry.SynchronizeMutation(() =>
            {
                IReadOnlyList<Extension> packageExtensions =
                    extensionRegistry.GetPackageExtensions(package.LocalId);
                if (packageExtensions.Any(extension => !SupportsLiveUnload(extension)))
                {
                    restartRequired = true;
                    return;
                }

                removal = extensionRegistry.RemoveExtensions(package.LocalId);
            });
        }
        catch (ExtensionRemovalNotificationException ex)
        {
            removal = ex.Removal;
            retirementFailures = [ex];
        }

        requiresRestart = restartRequired;
        return removal;
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

    private string[] CaptureAssemblyNames(LocalPackage package, PluginLoadContext loadContext)
    {
        try
        {
            // Capture only the assembly names as strings; never retain the assemblies/types, or the later
            // diagnostics pass would itself root the context it is meant to diagnose.
            return [.. loadContext.Assemblies
                .Select(a => a.GetName().Name)
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception ex)
        {
            // Best-effort like TryUnloadLoadContext: a reflection failure here must not break the unload flow.
            _logger.LogWarning(ex, "Failed to capture assembly names for unload diagnostics of {PackageName}.", package.Name);
            return [];
        }
    }

    private sealed record PackageUnloadResult(
        WeakReference LoadContextReference,
        string[] AssemblyNames);

    private static bool SupportsLiveUnload(Extension extension)
        => extension is ILiveUnloadExtension;

    // Test seam: a unit test substitutes this to assert the capture opens the written dump path without launching a
    // real process. Production leaves it as OpenDumpFile.
    internal Action<string> DumpOpener
    {
        get => _dumpOpener ??= OpenDumpFile;
        set => _dumpOpener = value;
    }

    // Diagnostics are wired only in Debug builds (BeutlApiApplication injects null in Release), so [Conditional]
    // strips this prompt the same way, keeping the offer in step with the capture it would trigger.
    [Conditional("DEBUG")]
    internal void PromptCaptureUnloadDiagnostics(
        ILoadContextUnloadDiagnostics diagnostics, string packageName, string[] assemblyNames)
    {
        NotificationService.ShowWarning(
            $"Failed to unload '{packageName}'",
            "The extension's load context is still alive. Capture a diagnostics dump to find what is keeping the "
            + "assemblies loaded?",
            expiration: TimeSpan.FromSeconds(30),
            actions:
            [
                // Offload: the ClrMD self-snapshot is heavy and must not block the UI thread the click runs on.
                new NotificationAction(
                    "Capture dump",
                    () => { _ = Task.Run(() => CaptureAndOpenUnloadDump(diagnostics, packageName, assemblyNames)); })
            ]);
    }

    // Synchronous so a test can drive it directly; production reaches it from the prompt action's Task.Run. Contained
    // because CaptureUnloadFailure is a public interface a third-party implementation could throw from.
    internal void CaptureAndOpenUnloadDump(
        ILoadContextUnloadDiagnostics diagnostics, string packageName, string[] assemblyNames)
    {
        try
        {
            string? dumpPath = diagnostics.CaptureUnloadFailure(packageName, assemblyNames);
            if (!string.IsNullOrEmpty(dumpPath))
            {
                DumpOpener(dumpPath);
            }
            else
            {
                // The capture ran but produced nothing (context already collected / another capture holds the gate /
                // snapshotting unsupported). The click already dismissed the prompt, so acknowledge it or it looks dead.
                NotifyDiagnosticsUnavailable(packageName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unload diagnostics capture threw for {PackageName}.", packageName);
        }
    }

    private static void NotifyDiagnosticsUnavailable(string packageName)
    {
        NotificationService.ShowInformation(
            $"No dump captured for '{packageName}'",
            "The load context was already collected, another capture is in progress, or snapshotting is "
            + "unavailable. See the log for details.");
    }

    private void OpenDumpFile(string dumpPath)
    {
        try
        {
            // UseShellExecute routes to the OS handler (ShellExecute / open / xdg-open) to open the .txt on any platform.
            Process.Start(new ProcessStartInfo(dumpPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open unload diagnostics dump at {DumpPath}.", dumpPath);
        }
    }
}
