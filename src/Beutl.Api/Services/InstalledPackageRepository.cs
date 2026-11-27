using System.Reactive;
using System.Reactive.Subjects;
using System.Text.Json;

using Beutl.Logging;
using Beutl.Reactive;

using Microsoft.Extensions.Logging;

using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.Api.Services;

public class InstalledPackageRepository : IBeutlApiResource
{
    private readonly ILogger _logger = Log.CreateLogger<InstalledPackageRepository>();
    private readonly Subject<(PackageIdentity Package, bool Exists)> _subject = new();
    private static string FilePath => Path.Combine(Helper.AppRoot, "installedPackages.json");
    private Snapshot _current;

    public InstalledPackageRepository()
    {
        _current = ReadSnapshot();
    }

    private Snapshot Current => Volatile.Read(ref _current);

    public bool RestoreFailed => Current.Incomplete;

    public IEnumerable<PackageIdentity> GetLocalPackages() => Current.Packages.Keys;

    public IEnumerable<PackageIdentity> GetLocalPackages(string name)
        => GetLocalPackages().Where(package => HasName(package, name));

    // Live extensions are per process. Installed package records belong to the profile.
    internal PackageIdentity[] GetProfilePackages() => [.. ReadSnapshot().Packages.Keys];

    internal bool TryGetCompleteProfilePackages(out PackageIdentity[] packages)
    {
        Snapshot snapshot = ReadSnapshot();
        packages = [.. snapshot.Packages.Keys];
        return !RestoreFailed && !snapshot.Incomplete;
    }

    internal void RequireNoReplacement(PackageIdentity package)
    {
        if (GetProfilePackages().FirstOrDefault(item => HasName(item, package.Id) && !package.Equals(item)) is { } installed)
            throw new PackageVersionConflictException(installed);
    }

    internal PackageIdentity? GetLatestLocalPackage(string name)
        => GetLocalPackages(name).MaxBy(package => package.Version);

    internal Action UpgradePackagesAndDeferNotifications(PackageIdentity package, bool allowReplacement = true)
    {
        using var lease = PackageFileLock.Acquire(FilePath + ".lock");
        if (ReadForWrite() is not { } packages)
        {
            throw new InvalidOperationException(
                "Cannot register an installed package because installedPackages.json could not be restored.");
        }

        PackageIdentity[] removedItems = [.. packages.Keys.Where(item => HasName(item, package.Id))];
        foreach (PackageIdentity item in removedItems)
        {
            if (!allowReplacement && !package.Equals(item))
                throw new PackageVersionConflictException(item);
            packages.Remove(item);
        }

        _logger.LogInformation("Registering package {PackageId} version {PackageVersion}.", package.Id, package.Version);
        packages.Add(package, BeutlApplication.Version);
        PublishSnapshot(packages);

        return () =>
        {
            foreach (PackageIdentity removed in removedItems)
                _subject.OnNext((removed, false));
            _subject.OnNext((package, true));
        };
    }

    private void ReportObserverFailure(Exception exception)
        => _logger.LogError(exception, "An installed-package observer failed while receiving a notification.");

    // Wrap each subscription, not only the subject: one returned observable can
    // itself have several observers through LightweightObservableBase.PublishNext.
    private sealed class ObserverIsolatingObservable<T>(IObservable<T> source, Action<Exception> failure) : IObservable<T>
    {
        public IDisposable Subscribe(IObserver<T> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            return source.Subscribe(Observer.Create<T>(
                value => Forward(() => observer.OnNext(value)),
                error => Forward(() => observer.OnError(error)),
                () => Forward(observer.OnCompleted)));

            void Forward(Action notification)
            {
                try { notification(); }
                catch (Exception ex) { failure(ex); }
            }
        }
    }

    public void RemovePackage(PackageIdentity package)
        => RemovePackagesWhere(package.Equals);

    public void RemovePackages(string name)
        => RemovePackagesWhere(package => HasName(package, name));

    private void RemovePackagesWhere(Func<PackageIdentity, bool> matches)
    {
        PackageIdentity[] removed;
        using (PackageFileLock.Acquire(FilePath + ".lock"))
        {
            if (ReadForWrite() is not { } packages) return;
            removed = [.. packages.Keys.Where(matches)];
            if (removed.Length == 0) return;
            foreach (PackageIdentity package in removed)
                packages.Remove(package);
            PublishSnapshot(packages);
        }

        foreach (PackageIdentity package in removed)
        {
            _logger.LogInformation("Removed package {PackageId} version {PackageVersion}.", package.Id, package.Version);
            _subject.OnNext((package, false));
        }
    }

    public bool ExistsPackage(PackageIdentity package)
        => Current.Packages.ContainsKey(package);

    public bool ExistsPackage(string name, string version)
        => ExistsPackage(new PackageIdentity(name, new NuGetVersion(version)));

    public bool ExistsPackage(string name)
        => GetLocalPackages(name).Any();

    public IObservable<bool> GetObservable(string name)
    {
        return new ObserverIsolatingObservable<bool>(new _Observable(this, name), ReportObserverFailure);
    }

    public IObservable<PackageIdentity?> GetPackageObservable(string name)
    {
        return new ObserverIsolatingObservable<PackageIdentity?>(new _PackageObservable(this, name), ReportObserverFailure);
    }

    public PackageIdentity[] GetPackagesNeedingDependencyReResolution()
    {
        string currentVersion = BeutlApplication.Version;
        return [.. Current.Packages.Where(item => item.Value != currentVersion).Select(item => item.Key)];
    }

    public void SetResolvedBeutlVersion(string packageId, string beutlVersion)
    {
        using var lease = PackageFileLock.Acquire(FilePath + ".lock");
        if (ReadForWrite() is not { } packages) return;
        PackageIdentity[] resolved = [.. packages.Keys.Where(package => HasName(package, packageId))];
        if (resolved.Length == 0) return;
        foreach (PackageIdentity package in resolved)
            packages[package] = beutlVersion;
        PublishSnapshot(packages);
    }

    private Dictionary<PackageIdentity, string?>? ReadForWrite()
    {
        // Reload under the file lock so a running process cannot overwrite a change from another.
        if (!RestoreFailed && ReadSnapshot() is { Incomplete: false } snapshot)
            return snapshot.Packages;
        _logger.LogWarning("Skipping save of installed packages because installedPackages.json could not be restored.");
        return null;
    }

    private void PublishSnapshot(Dictionary<PackageIdentity, string?> packages)
    {
        var entries = packages.Select(x => new S_Package(x.Key.Id, x.Key.Version.ToString(), x.Value));
        JsonSerializer.SerializeToNode(entries.ToArray())!.JsonSave(FilePath);
        Volatile.Write(ref _current, new Snapshot(packages, Incomplete: false));
    }

    private Snapshot ReadSnapshot()
    {
        string fileName = FilePath;
        var packages = new Dictionary<PackageIdentity, string?>(PackageIdentityComparer.Default);
        int skipped = 0;
        try
        {
            using var stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            using JsonDocument document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new JsonException("The installed packages file does not contain an array.");

            int index = -1;
            foreach (JsonElement element in document.RootElement.EnumerateArray())
            {
                index++;
                try
                {
                    S_Package? package = element.Deserialize<S_Package>();
                    if (package is null)
                    {
                        skipped++;
                        _logger.LogWarning("Skipping a null installed package entry at index {Index}.", index);
                        continue;
                    }

                    packages[new PackageIdentity(package.Name, new NuGetVersion(package.Version))] = package.BeutlVersion;
                }
                catch (Exception ex) when (ex is JsonException or ArgumentException or FormatException)
                {
                    skipped++;
                    _logger.LogWarning(ex, "Skipping invalid installed package entry at index {Index}.", index);
                }
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new Snapshot(packages, Incomplete: false);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogError(ex, "Failed to restore packages from file; keeping the file unchanged.");
            KeepBackup(fileName);
            return new Snapshot(packages, Incomplete: true);
        }

        // Keep the incomplete registry unchanged so protection remains active after restart.
        if (skipped > 0) KeepBackup(fileName);
        return new Snapshot(packages, Incomplete: skipped > 0);
    }

    private string? KeepBackup(string fileName)
    {
        string backup = fileName + ".bak";
        try
        {
            File.Copy(fileName, backup, overwrite: true);
            return backup;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not keep a backup of installedPackages.json.");
            return null;
        }
    }

    // Serializable
    private record S_Package(string Name, string Version, string? BeutlVersion = null);

    private sealed record Snapshot(Dictionary<PackageIdentity, string?> Packages, bool Incomplete);

    private static bool HasName(PackageIdentity package, string name)
        => StringComparer.OrdinalIgnoreCase.Equals(package.Id, name);

    private sealed class _Observable : LightweightObservableBase<bool>
    {
        private readonly InstalledPackageRepository _repository;
        private readonly string _name;
        private IDisposable? _disposable;

        public _Observable(InstalledPackageRepository repository, string name)
        {
            _repository = repository;
            _name = name;
        }

        protected override void Subscribed(IObserver<bool> observer, bool first)
        {
            observer.OnNext(_repository.ExistsPackage(_name));
        }

        protected override void Deinitialize()
        {
            _disposable?.Dispose();
            _disposable = null;
        }

        protected override void Initialize()
        {
            _disposable = _repository._subject
                .Subscribe(OnReceived);
        }

        private void OnReceived((PackageIdentity Package, bool Exists) obj)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(obj.Package.Id, _name))
            {
                // Per-event Exists is unreliable for name-only observers; re-evaluate the aggregate.
                PublishNext(_repository.ExistsPackage(_name));
            }
        }
    }

    private sealed class _PackageObservable : LightweightObservableBase<PackageIdentity?>
    {
        private readonly InstalledPackageRepository _repository;
        private readonly string _name;
        private IDisposable? _disposable;

        public _PackageObservable(InstalledPackageRepository repository, string name)
        {
            _repository = repository;
            _name = name;
        }

        protected override void Subscribed(IObserver<PackageIdentity?> observer, bool first)
        {
            observer.OnNext(GetLatestInstalled());
        }

        protected override void Deinitialize()
        {
            _disposable?.Dispose();
            _disposable = null;
        }

        protected override void Initialize()
        {
            _disposable = _repository._subject.Subscribe(OnReceived);
        }

        private void OnReceived((PackageIdentity Package, bool Exists) obj)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(obj.Package.Id, _name))
            {
                PublishNext(GetLatestInstalled());
            }
        }

        private PackageIdentity? GetLatestInstalled()
        {
            return _repository.GetLatestLocalPackage(_name);
        }
    }
}

internal sealed class PackageVersionConflictException(PackageIdentity installed)
    : InvalidOperationException($"'{installed.Id}' version {installed.Version} is already installed.")
{
    public PackageIdentity Installed { get; } = installed;
}
