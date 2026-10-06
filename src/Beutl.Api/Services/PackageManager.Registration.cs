using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Platform;
using Beutl.Engine;
using Beutl.Extensibility;
using Microsoft.Extensions.Logging;

namespace Beutl.Api.Services;

public sealed partial class PackageManager
{
    internal Assembly[] LoadExtensionsAndRegister(
        Activity? activity,
        LocalPackage package,
        Assembly[] assemblies,
        PluginLoadContext? loadContext,
        IEnumerable<Type> extensionTypes)
    {
        List<Extension> extensions = [];
        var addedToProvider = false;
        var addedToLoadedPackages = false;
        if (!TryBeginLoading(package))
        {
            // The caller resolved the package's assemblies into a collectible
            // context before this could be known. Nothing will ever reference
            // them, so the context goes with the rejection rather than staying
            // loaded for the life of the process.
            if (loadContext is { })
            {
                TryUnloadLoadContext(package, loadContext);
            }

            throw new InvalidOperationException(
                $"Package {package.Name} is already loaded, loading, unloading, or quarantined.");
        }

        try
        {
            extensions = LoadPackageExtensions(extensionTypes);

            activity?.AddEvent(new ActivityEvent("Extensions loaded"));
            activity?.SetTag("ExtensionCount", extensions.Count);

            ExtensionRegistry.AddExtensions(package.LocalId, extensions);
            addedToProvider = true;
            AfterExtensionRegistration?.Invoke();

            lock (_packageLifecycleGate)
            {
                if (!_loadedPackages.TryAdd(
                        package.LocalId,
                        new LoadedPackageInfo(package, loadContext)))
                {
                    throw new InvalidOperationException(
                        $"Package {package.Name} is already loaded, unloading, or quarantined.");
                }

                _loadingPackages.Remove(package.LocalId);
            }
            addedToLoadedPackages = true;

            return assemblies;
        }
        catch (Exception loadFailure)
        {
            RollBackFailedRegistration(
                package,
                extensions,
                loadContext,
                loadFailure,
                addedToProvider,
                addedToLoadedPackages);
            throw;
        }
    }

    private bool TryBeginLoading(LocalPackage package)
    {
        lock (_packageLifecycleGate)
        {
            bool alreadyKnown = _loadingPackages.Contains(package.LocalId)
                                || _unloadOperations.ContainsKey(package.LocalId)
                                || _quarantinedPackages.Contains(package.LocalId)
                                || _loadedPackages.ContainsKey(package.LocalId);
            if (!alreadyKnown)
            {
                _loadingPackages.Add(package.LocalId);
            }

            return !alreadyKnown;
        }
    }

    private void RollBackFailedRegistration(
        LocalPackage package,
        List<Extension> extensions,
        PluginLoadContext? loadContext,
        Exception loadFailure,
        bool addedToProvider,
        bool addedToLoadedPackages)
    {
        ExtensionRemoval? rollbackRemoval = null;
        if (loadFailure is ExtensionRegistrationNotificationException registrationFailure)
        {
            rollbackRemoval = registrationFailure.Removal;
        }
        if (addedToProvider)
        {
            try
            {
                rollbackRemoval = ExtensionRegistry.RemoveExtensions(package.LocalId);
            }
            catch (ExtensionRemovalNotificationException ex)
            {
                rollbackRemoval = ex.Removal;
            }
        }
        if (addedToLoadedPackages)
        {
            lock (_packageLifecycleGate)
            {
                _loadedPackages.TryRemove(package.LocalId, out _);
            }
        }

        bool rollbackPending = rollbackRemoval is not null;
        lock (_packageLifecycleGate)
        {
            _loadingPackages.Remove(package.LocalId);
            if (rollbackPending)
            {
                _quarantinedPackages.Add(package.LocalId);
            }
        }

        if (rollbackRemoval is null)
        {
            // LoadPackageExtensions already rolls back on failure, so extensions is non-empty
            // only when a later registration step threw; this is not a double-unload.
            RollbackLoadedExtensions(extensions);
            if (loadContext is { })
            {
                TryUnloadLoadContext(package, loadContext);
            }
        }
        else
        {
            StartRollbackAfterDrain(
                package,
                extensions,
                loadContext,
                rollbackRemoval);
        }
    }

    private void StartRollbackAfterDrain(
        LocalPackage package,
        List<Extension> extensions,
        PluginLoadContext? loadContext,
        ExtensionRemoval removal)
    {
        Extension[] rollbackExtensions = extensions.ToArray();
        extensions.Clear();
        var pendingRollback = new PendingRollbackInfo(
            package,
            rollbackExtensions,
            loadContext);
        lock (_packageLifecycleGate)
        {
            _pendingRollbacks.Add(package.LocalId, pendingRollback);
        }
        pendingRollback.Operation = DrainAndRollbackAsync(
            pendingRollback,
            removal);
    }

    private async Task DrainAndRollbackAsync(
        PendingRollbackInfo pendingRollback,
        ExtensionRemoval removal)
    {
        try
        {
            await removal.DrainAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            lock (_packageLifecycleGate)
            {
                _quarantinedPackages.Add(pendingRollback.Package.LocalId);
            }
            _logger.LogError(
                ex,
                "Package {PackageName} failed to drain after registration rollback; its load context remains quarantined.",
                pendingRollback.Package.Name);
            return;
        }

        if (pendingRollback.Extensions.Any(extension => !SupportsLiveUnload(extension)))
        {
            _logger.LogWarning(
                "Package {PackageName} exposed restart-only extensions before registration rollback; "
                + "the extensions and load context remain quarantined until restart.",
                pendingRollback.Package.Name);
            return;
        }

        var rollbackList = pendingRollback.Extensions.ToList();
        RollbackLoadedExtensions(rollbackList);
        if (pendingRollback.LoadContext is not null)
        {
            TryUnloadLoadContext(pendingRollback.Package, pendingRollback.LoadContext);
        }
        lock (_packageLifecycleGate)
        {
            _pendingRollbacks.Remove(pendingRollback.Package.LocalId);
            _quarantinedPackages.Remove(pendingRollback.Package.LocalId);
        }
    }

    private sealed class PendingRollbackInfo(
        LocalPackage package,
        IReadOnlyList<Extension> extensions,
        PluginLoadContext? loadContext)
    {
        public LocalPackage Package { get; } = package;

        public IReadOnlyList<Extension> Extensions { get; } = extensions;

        public PluginLoadContext? LoadContext { get; } = loadContext;

        public Task? Operation { get; set; }
    }

    internal List<Extension> LoadPackageExtensions(IEnumerable<Type> extensionTypes)
    {
        var extensions = new List<Extension>();
        try
        {
            foreach (Type type in extensionTypes)
            {
                LoadExtension(type, extensions);
            }

            return extensions;
        }
        catch
        {
            RollbackLoadedExtensions(extensions);
            throw;
        }
    }

    private void LoadExtension(Type type, List<Extension> extensions)
    {
        if (type.GetCustomAttribute<ExportAttribute>() is { }
            && type.IsAssignableTo(typeof(Extension))
            && Activator.CreateInstance(type) is Extension extension)
        {
            var loadStarted = false;
            try
            {
                SetupExtensionSettings(extension);
                if (extension is ViewExtension viewExtension)
                {
                    commandManager.Register(viewExtension);
                }

                loadStarted = true;
                extension.Load();

                extensions.Add(extension);
                _logger.LogInformation("Extension {ExtensionName} loaded from assembly {AssemblyName}", type.Name, type.Assembly.GetName().Name);
            }
            catch
            {
                RollbackExtensionLoad(extension, loadStarted);
                throw;
            }
        }
    }

    private void RollbackLoadedExtensions(List<Extension> extensions)
    {
        for (int i = extensions.Count - 1; i >= 0; i--)
        {
            RollbackExtensionLoad(extensions[i], unload: true);
        }

        extensions.Clear();
    }

    private void RollbackExtensionLoad(Extension extension, bool unload)
    {
        if (extension is ViewExtension viewExtension)
        {
            try
            {
                commandManager.Unregister(viewExtension);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to unregister commands while rolling back extension {ExtensionName}.", extension.GetType().Name);
            }
        }

        if (unload)
        {
            try
            {
                extension.Unload();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to unload extension {ExtensionName} while rolling back load.", extension.GetType().Name);
            }
        }

        try
        {
            CleanupExtensionSettings(extension);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean up settings while rolling back extension {ExtensionName}.", extension.GetType().Name);
        }
    }

    private void TryUnloadLoadContext(LocalPackage package, PluginLoadContext loadContext)
    {
        try
        {
            Type[] types = loadContext.Assemblies.SelectMany(GetLoadableTypes).ToArray();
            TypeUnloadNotifier.NotifyUnloading(types);
            AvaloniaPropertyRegistry.Instance.UnregisterByModule(types);
            foreach (string name in loadContext.Assemblies.Select(a => a.GetName().Name).OfType<string>())
            {
                AssetLoader.InvalidateAssemblyCache(name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean up type registrations for {PackageName}.", package.Name);
        }

        try
        {
            loadContext.Unload();
            _logger.LogInformation("AssemblyLoadContext unloaded for {PackageName}.", package.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unload AssemblyLoadContext for {PackageName}.", package.Name);
        }
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }

    internal void SetupExtensionSettings(Extension extension)
    {
        if (extension.Settings is { } settings)
        {
            _settingsStore.Restore(extension, settings);

            EventHandler handler = (_, _) => _settingsStore.Save(extension, settings);
            extension.SettingsChangedHandler = handler;
            settings.ConfigurationChanged += handler;
            _logger.LogInformation("Settings restored for extension {ExtensionName}", extension.GetType().Name);
        }
    }

    private void CleanupExtensionSettings(Extension extension)
    {
        if (extension.Settings is { } settings && extension.SettingsChangedHandler is { } handler)
        {
            settings.ConfigurationChanged -= handler;
            extension.SettingsChangedHandler = null;
        }
    }
}
