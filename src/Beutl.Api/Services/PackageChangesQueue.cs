using System.Reactive.Subjects;

using Beutl.Reactive;

using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Beutl.Api.Services;

public class PackageChangesQueue : IBeutlApiResource
{
    private readonly HashSet<PackageIdentity> _installs = [];
    private readonly HashSet<PackageIdentity> _uninstalls = [];

    private readonly Subject<(PackageIdentity Package, EventType Type)> _subject = new();

    public enum EventType
    {
        None,
        Install,
        Uninstall,
    }

    public IEnumerable<PackageIdentity> GetInstalls()
    {
        return _installs;
    }

    public IEnumerable<PackageIdentity> GetUninstalls()
    {
        return _uninstalls;
    }

    public IObservable<EventType> GetObservable(string name, string? version = null)
    {
        return new _Observable(this, name, version);
    }

    public void UninstallQueue(PackageIdentity packageIdentity)
    {
        _installs.Remove(packageIdentity);
        _uninstalls.Add(packageIdentity);
        _subject.OnNext((packageIdentity, EventType.Uninstall));
    }

    public void InstallQueue(PackageIdentity packageIdentity)
    {
        _uninstalls.Remove(packageIdentity);
        _installs.Add(packageIdentity);
        _subject.OnNext((packageIdentity, EventType.Install));
    }

    public void Cancel(PackageIdentity packageIdentity)
    {
        _uninstalls.Remove(packageIdentity);
        _installs.Remove(packageIdentity);
        _subject.OnNext((packageIdentity, EventType.None));
    }

    public void Cancel(string name)
    {
        var cancels = new HashSet<PackageIdentity>();
        foreach (PackageIdentity item in _uninstalls)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(item.Id, name))
            {
                cancels.Add(item);
                _uninstalls.Remove(item);
            }
        }
        foreach (PackageIdentity item in _installs)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(item.Id, name))
            {
                cancels.Add(item);
                _installs.Remove(item);
            }
        }

        foreach (PackageIdentity item in cancels)
        {
            _subject.OnNext((item, EventType.None));
        }
    }

    private sealed class _Observable : LightweightObservableBase<EventType>
    {
        private readonly PackageChangesQueue _queue;
        private readonly string _name;
        private readonly PackageIdentity? _packageIdentity;
        private IDisposable? _disposable;

        public _Observable(PackageChangesQueue queue, string name, string? version)
        {
            _queue = queue;
            _name = name;

            if (version is { })
            {
                _packageIdentity = new PackageIdentity(name, new NuGetVersion(version));
            }
        }

        protected override void Subscribed(IObserver<EventType> observer, bool first)
        {
            if (_packageIdentity is { })
            {
                if (_queue._installs.Contains(_packageIdentity))
                {
                    observer.OnNext(EventType.Install);
                }
                else if (_queue._uninstalls.Contains(_packageIdentity))
                {
                    observer.OnNext(EventType.Uninstall);
                }
                else
                {
                    observer.OnNext(EventType.None);
                }
            }
            else
            {
                if (_queue._installs.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x.Id, _name)))
                {
                    observer.OnNext(EventType.Install);
                }
                else if (_queue._uninstalls.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x.Id, _name)))
                {
                    observer.OnNext(EventType.Uninstall);
                }
                else
                {
                    observer.OnNext(EventType.None);
                }
            }
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
            if ((_packageIdentity != null && _packageIdentity == obj.Package)
                || StringComparer.OrdinalIgnoreCase.Equals(obj.Package.Id, _name))
            {
                PublishNext(obj.Type);
            }
        }
    }
}
