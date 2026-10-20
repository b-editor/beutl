using System.Reactive.Subjects;
using System.Text.Json;

using Beutl.Logging;
using Beutl.Reactive;

using Microsoft.Extensions.Logging;

using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.Api.Services;

// Saved on every change so that a crash keeps the queue; PackageTools.UI reads the file after Beutl exits.
public class PackageChangesQueue : IBeutlApiResource
{
    private readonly ILogger _logger = Log.CreateLogger<PackageChangesQueue>();
    private readonly Lock _gate = new();
    private readonly string _path;
    private readonly HashSet<PackageIdentity> _installs = [];
    private readonly HashSet<PackageIdentity> _uninstalls = [];

    private readonly Subject<(PackageIdentity Package, EventType Type)> _subject = new();

    public PackageChangesQueue()
        : this(Path.Combine(Helper.AppRoot, "packageChanges.json"))
    {
    }

    internal PackageChangesQueue(string path)
    {
        _path = path;
        Restore();
    }

    public enum EventType
    {
        None,
        Install,
        Uninstall,
    }

    public IEnumerable<PackageIdentity> GetInstalls()
    {
        lock (_gate)
        {
            return [.. _installs];
        }
    }

    public IEnumerable<PackageIdentity> GetUninstalls()
    {
        lock (_gate)
        {
            return [.. _uninstalls];
        }
    }

    public IObservable<EventType> GetObservable(string name)
    {
        return new _Observable(this, name);
    }

    public void UninstallQueue(PackageIdentity packageIdentity)
    {
        lock (_gate)
        {
            _installs.Remove(packageIdentity);
            _uninstalls.Add(packageIdentity);
            Save();
        }
        _subject.OnNext((packageIdentity, EventType.Uninstall));
    }

    public void InstallQueue(PackageIdentity packageIdentity)
    {
        lock (_gate)
        {
            _uninstalls.Remove(packageIdentity);
            _installs.Add(packageIdentity);
            Save();
        }
        _subject.OnNext((packageIdentity, EventType.Install));
    }

    public void Cancel(string name)
    {
        PackageIdentity[] cancels;
        lock (_gate)
        {
            cancels = [.. _uninstalls.Concat(_installs).Where(item => StringComparer.OrdinalIgnoreCase.Equals(item.Id, name))];
            _uninstalls.ExceptWith(cancels);
            _installs.ExceptWith(cancels);
            if (cancels.Length > 0)
                Save();
        }

        foreach (PackageIdentity item in cancels)
        {
            _subject.OnNext((item, EventType.None));
        }
    }

    // PackageTools.UI removes a change once it has run it, whatever the result.
    internal void Remove(PackageIdentity packageIdentity)
    {
        EventType state;
        lock (_gate)
        {
            if (!(_installs.Remove(packageIdentity) | _uninstalls.Remove(packageIdentity)))
                return;

            Save();
            state = GetState_NoLock(packageIdentity.Id);
        }
        _subject.OnNext((packageIdentity, state));
    }

    private EventType GetState_NoLock(string name)
    {
        if (_installs.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x.Id, name)))
            return EventType.Install;

        return _uninstalls.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x.Id, name))
            ? EventType.Uninstall
            : EventType.None;
    }

    // A failed write keeps the change for this session only.
    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var changes = new S_Changes(
                [.. _installs.Select(S_Package.From)],
                [.. _uninstalls.Select(S_Package.From)]);
            JsonSerializer.SerializeToNode(changes)!.JsonSave(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Unable to save the package changes to {Path}.", _path);
        }
    }

    // A missing, unreadable or unexpected file reads as an empty queue.
    private void Restore()
    {
        if (!File.Exists(_path))
            return;

        try
        {
            using FileStream stream = File.OpenRead(_path);
            if (JsonSerializer.Deserialize<S_Changes>(stream) is { } changes)
            {
                _installs.UnionWith(changes.Installs.Select(package => package.ToIdentity()));
                _uninstalls.UnionWith(changes.Uninstalls.Select(package => package.ToIdentity()));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to read the package changes from {Path}.", _path);
            _installs.Clear();
            _uninstalls.Clear();
        }
    }

    // Serializable
    private sealed record S_Changes(S_Package[] Installs, S_Package[] Uninstalls);

    private sealed record S_Package(string Name, string? Version)
    {
        public static S_Package From(PackageIdentity package)
            => new(package.Id, package.HasVersion ? package.Version.ToFullString() : null);

        public PackageIdentity ToIdentity()
            => new(Name, Version is null ? null : NuGetVersion.Parse(Version));
    }

    private sealed class _Observable : LightweightObservableBase<EventType>
    {
        private readonly PackageChangesQueue _queue;
        private readonly string _name;
        private IDisposable? _disposable;

        public _Observable(PackageChangesQueue queue, string name)
        {
            _queue = queue;
            _name = name;
        }

        protected override void Subscribed(IObserver<EventType> observer, bool first)
        {
            EventType state;
            lock (_queue._gate)
            {
                state = _queue.GetState_NoLock(_name);
            }
            observer.OnNext(state);
        }

        protected override void Deinitialize()
        {
            _disposable?.Dispose();
            _disposable = null;
        }

        protected override void Initialize()
        {
            _disposable = _queue._subject
                .Subscribe(OnReceived);
        }

        private void OnReceived((PackageIdentity Package, EventType Type) obj)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(obj.Package.Id, _name))
            {
                PublishNext(obj.Type);
            }
        }
    }
}
