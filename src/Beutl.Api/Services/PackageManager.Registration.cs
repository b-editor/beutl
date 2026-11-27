using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Platform;
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
        if (!TryBeginLoading(package.LocalId))
        {
            // The caller resolved the package's assemblies into a collectible
            // context before this could be known. Nothing will ever reference
            // them, so the context goes with the rejection rather than staying
            // loaded for the life of the process.
            if (loadContext is { })
            {
                TryUnloadLoadContext(package.Name, loadContext);
            }

            throw new InvalidOperationException(
                $"Package {package.Name} is already loaded, loading, unloading, or quarantined.");
        }

        try
        {
            extensions = LoadPackageExtensions(extensionTypes);
            foreach (Extension extension in extensions)
            {
                ExtensionGuard.SetPackage(extension, package.Name);
            }

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
            return assemblies;
        }
        catch (Exception loadFailure)
        {
            RollBackFailedRegistration(
                package.LocalId,
                package.Name,
                extensions,
                loadContext,
                loadFailure,
                addedToProvider);
            if (loadContext is not null)
            {
                throw CreateDetachedLoadFailure(loadFailure);
            }

            throw;
        }
    }

    private static Exception CreateDetachedLoadFailure(Exception loadFailure)
    {
        // An exception can root plugin code through its fields or its captured stack frames.
        string typeName = loadFailure.GetType().ToString();
        string message = typeName;
        string diagnostics;
        try
        {
            message = $"{typeName}: {loadFailure.Message}";
            diagnostics = loadFailure.ToString();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A plugin's exception formatter can itself throw from collectible code.
            diagnostics = new StackTrace(loadFailure, fNeedFileInfo: true).ToString();
        }

        return ExceptionDispatchInfo.SetRemoteStackTrace(new InvalidOperationException(message), diagnostics);
    }

    private bool TryBeginLoading(int packageId)
    {
        lock (_packageLifecycleGate)
        {
            bool alreadyKnown = _loadingPackages.Contains(packageId)
                                || _unloadOperations.ContainsKey(packageId)
                                || _quarantinedPackages.Contains(packageId)
                                || _loadedPackages.ContainsKey(packageId);
            if (!alreadyKnown)
            {
                _loadingPackages.Add(packageId);
            }

            return !alreadyKnown;
        }
    }

    private void RollBackFailedRegistration(
        int packageId,
        string packageName,
        List<Extension> extensions,
        PluginLoadContext? loadContext,
        Exception loadFailure,
        bool addedToProvider)
    {
        ExtensionRemoval? rollbackRemoval = (loadFailure as ExtensionRegistrationNotificationException)?.Removal;
        if (addedToProvider)
        {
            try
            {
                rollbackRemoval = ExtensionRegistry.RemoveExtensions(packageId);
            }
            catch (ExtensionRemovalNotificationException ex)
            {
                rollbackRemoval = ex.Removal;
            }
        }
        if (rollbackRemoval is not null)
        {
            lock (_packageLifecycleGate)
            {
                _loadingPackages.Remove(packageId);
                _quarantinedPackages.Add(packageId);
            }

            _ = DrainAndRollbackAsync(packageId, packageName, extensions, loadContext, rollbackRemoval);
            return;
        }

        RollbackLoadedExtensions(extensions);
        if (loadContext is not null)
        {
            TryUnloadLoadContext(packageName, loadContext);
        }

        lock (_packageLifecycleGate)
        {
            _loadingPackages.Remove(packageId);
        }
    }

    private async Task DrainAndRollbackAsync(
        int packageId,
        string packageName,
        List<Extension> extensions,
        PluginLoadContext? loadContext,
        ExtensionRemoval removal)
    {
        try
        {
            await removal.DrainAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Package {PackageName} failed to drain after registration rollback and remains quarantined.",
                packageName);
            return;
        }

        RollbackLoadedExtensions(extensions);
        if (loadContext is not null)
        {
            TryUnloadLoadContext(packageName, loadContext);
        }
        lock (_packageLifecycleGate)
        {
            _quarantinedPackages.Remove(packageId);
        }
    }

    internal void LoadBuiltInExtensions(
        int packageId,
        IReadOnlyList<Extension> extensions,
        IReadOnlyList<Extension>? restoreSettingsFirst)
    {
        List<Extension> loaded = new(extensions.Count);
        HashSet<Extension>? pendingSettings = null;
        if (!TryBeginLoading(packageId))
        {
            throw new InvalidOperationException($"Extensions for package (id: {packageId}) are already loaded, loading, or rolling back.");
        }

        try
        {
            // Embedded backends restore settings for a subset before any extension loads.
            if (restoreSettingsFirst is not null)
            {
                pendingSettings = new(ReferenceEqualityComparer.Instance);
                foreach (Extension extension in restoreSettingsFirst)
                {
                    pendingSettings.Add(extension);
                    SetupExtensionSettings(extension);
                }
            }

            foreach (Extension extension in extensions)
            {
                pendingSettings?.Remove(extension);
                LoadExtension(extension, restoreSettings: restoreSettingsFirst is null);
                loaded.Add(extension);
            }

            ExtensionRegistry.AddExtensions(packageId, extensions);
            lock (_packageLifecycleGate)
            {
                _loadingPackages.Remove(packageId);
            }
        }
        catch (Exception loadFailure)
        {
            if (pendingSettings is not null)
            {
                foreach (Extension extension in pendingSettings)
                {
                    RollbackExtensionSettings(extension);
                }
            }

            RollBackFailedRegistration(
                packageId,
                $"built-in extensions (id: {packageId})",
                loaded,
                loadContext: null,
                loadFailure,
                addedToProvider: false);
            throw;
        }
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
            LoadExtension(extension);
            extensions.Add(extension);
            _logger.LogInformation("Extension {ExtensionName} loaded from assembly {AssemblyName}", type.Name, type.Assembly.GetName().Name);
        }
    }

    internal void LoadExtension(Extension extension, bool restoreSettings = true)
    {
        var loadStarted = false;
        try
        {
            if (restoreSettings)
                SetupExtensionSettings(extension);
            commandManager.RegisterExtension(extension);

            loadStarted = true;
            extension.Load(ExtensionContext);
        }
        catch
        {
            RollbackExtensionLoad(extension, loadStarted);
            throw;
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
        try
        {
            commandManager.UnregisterExtension(extension);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unregister commands while rolling back extension {ExtensionName}.", extension.GetType().Name);
        }

        if (unload)
        {
            try
            {
                extension.Unload(ExtensionContext);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to unload extension {ExtensionName} while rolling back load.", extension.GetType().Name);
            }
        }

        RollbackExtensionSettings(extension);
    }

    private void RollbackExtensionSettings(Extension extension)
    {
        try
        {
            CleanupExtensionSettings(extension);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean up settings while rolling back extension {ExtensionName}.", extension.GetType().Name);
        }
    }

    private void TryUnloadLoadContext(string packageName, PluginLoadContext loadContext)
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
            _logger.LogError(ex, "Failed to clean up type registrations for {PackageName}.", packageName);
        }

        try
        {
            loadContext.Unload();
            _logger.LogInformation("AssemblyLoadContext unloaded for {PackageName}.", packageName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unload AssemblyLoadContext for {PackageName}.", packageName);
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
