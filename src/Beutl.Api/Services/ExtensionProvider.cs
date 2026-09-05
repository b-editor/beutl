using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using Beutl.Collections;
using Beutl.Configuration;
using Beutl.Extensibility;

using static Beutl.Configuration.ExtensionConfig;

namespace Beutl.Api.Services;

public sealed class ExtensionProvider : IExtensionRegistry
{
    private readonly Dictionary<int, Extension[]> _allExtensions = [];
    private readonly ExtensionConfig _config = GlobalConfiguration.Instance.ExtensionConfig;
    private readonly Dictionary<Type, Array> _cache = [];
    private readonly ExtensionCollection _extensions = new();
    private readonly object _lock = new();
    private readonly object _mutationGate = new();
    private Extension[] _snapshot = [];

    public ExtensionProvider()
    {
    }

    public ICoreReadOnlyList<Extension> AllExtensions => _extensions;

    public TExtension[] GetExtensions<TExtension>()
        where TExtension : Extension
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(typeof(TExtension), out Array? result))
            {
                return (TExtension[])result;
            }
            else
            {
                TExtension[] exts = _snapshot.OfType<TExtension>().ToArray();
                _cache[typeof(TExtension)] = exts;
                return exts;
            }
        }
    }

    public IReadOnlyList<Extension> GetPackageExtensions(int packageId)
    {
        lock (_lock)
        {
            return _allExtensions.TryGetValue(packageId, out Extension[]? extensions)
                ? [.. extensions]
                : [];
        }
    }

    public EditorExtension? MatchEditorExtension(string file)
    {
        lock (_lock)
        {
            string? fileExt = Path.GetExtension(file);

            if (_config.EditorExtensions.TryGetValue(fileExt, out ICoreList<TypeLazy>? list))
            {
                foreach (Extension extension in _snapshot)
                {
                    Type extType = extension.GetType();
                    if (extension is not EditorExtension editorExtension) continue;

                    foreach (TypeLazy type in list.GetMarshal().Value)
                    {
                        if (extType == type.Type
                            && editorExtension.IsSupported(file))
                        {
                            return editorExtension;
                        }
                    }
                }
            }

            foreach (Extension extension in _snapshot)
            {
                if (extension is EditorExtension editorExtension &&
                    editorExtension.IsSupported(file))
                {
                    return editorExtension;
                }
            }

            return null;
        }
    }

    public ProjectItemExtension? MatchProjectItemExtension(string file)
    {
        lock (_lock)
        {
            foreach (Extension extension in _snapshot)
            {
                if (extension is ProjectItemExtension wsiExtension &&
                    wsiExtension.IsSupported(file))
                {
                    return wsiExtension;
                }
            }

            return null;
        }
    }

    public IEnumerable<ProjectItemExtension> MatchProjectItemExtensions(string file)
    {
        ProjectItemExtension[] result;
        lock (_lock)
        {
            result = _snapshot
                .OfType<ProjectItemExtension>()
                .Where(extension => extension.IsSupported(file))
                .ToArray();
        }

        return result;
    }

    public void AddExtensions(int packageId, IReadOnlyList<Extension> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        Extension[] ownedExtensions = extensions.ToArray();
        if (ownedExtensions.Any(extension => extension is null))
            throw new ArgumentException("Extensions cannot contain null.", nameof(extensions));

        lock (_mutationGate)
        {
            lock (_lock)
            {
                if (!_allExtensions.TryAdd(packageId, ownedExtensions))
                {
                    throw new InvalidOperationException(
                        $"Extensions for package (id: {packageId}) are already registered.");
                }

                _snapshot = [.. _snapshot, .. ownedExtensions];
                _cache.Clear();
            }

            // Collection observers may perform package-lifetime cleanup. Run them without the
            // provider lock so an in-flight extension operation can still query the provider.
            try
            {
                _extensions.AddRange(ownedExtensions);
            }
            catch (Exception registrationFailure)
            {
                lock (_lock)
                {
                    _allExtensions.Remove(packageId);
                    var ownedSet = new HashSet<Extension>(
                        ownedExtensions,
                        ReferenceEqualityComparer.Instance);
                    _snapshot = _snapshot
                        .Where(extension => !ownedSet.Contains(extension))
                        .ToArray();
                    _cache.Clear();
                }
                try
                {
                    _extensions.RemoveAll(ownedExtensions);
                }
                catch (Exception removalFailure)
                {
                    registrationFailure = new AggregateException(
                        registrationFailure,
                        removalFailure);
                }
                throw new ExtensionRegistrationNotificationException(
                    new ExtensionRemoval(ownedExtensions),
                    registrationFailure);
            }
        }
    }

    public ExtensionRemoval RemoveExtensions(int packageId)
    {
        lock (_mutationGate)
        {
            Extension[] extensions;
            lock (_lock)
            {
                if (!_allExtensions.Remove(packageId, out Extension[]? removed))
                {
                    return new ExtensionRemoval([]);
                }

                extensions = removed;
                var removedSet = new HashSet<Extension>(extensions, ReferenceEqualityComparer.Instance);
                _snapshot = _snapshot.Where(extension => !removedSet.Contains(extension)).ToArray();
                _cache.Clear();
            }

            // Caption and other dynamic catalogs retire their package-owned registrations from
            // this synchronous notification before PackageManager invokes Extension.Unload().
            Exception? notificationFailure = null;
            try
            {
                _extensions.RemoveAll(extensions);
            }
            catch (Exception ex)
            {
                notificationFailure = ex;
            }
            ExtensionRemoval removal = new(extensions);
            if (notificationFailure is not null)
                throw new ExtensionRemovalNotificationException(removal, notificationFailure);

            return removal;
        }
    }

    public void SynchronizeMutation(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_mutationGate)
        {
            action();
        }
    }

    private sealed class ExtensionCollection : ICoreReadOnlyList<Extension>
    {
        private static readonly PropertyChangedEventArgs s_countChanged = new(nameof(Count));
        private static readonly PropertyChangedEventArgs s_indexerChanged = new("Item[]");
        private readonly List<Extension> _items = [];
        private readonly object _gate = new();

        public event NotifyCollectionChangedEventHandler? CollectionChanged;

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _items.Count;
                }
            }
        }

        public Extension this[int index]
        {
            get
            {
                lock (_gate)
                {
                    return _items[index];
                }
            }
        }

        public IEnumerator<Extension> GetEnumerator()
        {
            lock (_gate)
            {
                return ((IEnumerable<Extension>)_items.ToArray()).GetEnumerator();
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void AddRange(IReadOnlyList<Extension> extensions)
        {
            if (extensions.Count == 0)
                return;

            Extension[] added = extensions.ToArray();
            int index;
            lock (_gate)
            {
                index = _items.Count;
                _items.AddRange(added);
            }

            NotifyObservers(new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Add,
                (IList)added,
                index));
        }

        public void RemoveAll(IReadOnlyList<Extension> extensions)
        {
            if (extensions.Count == 0)
                return;

            var removedSet = new HashSet<Extension>(
                extensions,
                ReferenceEqualityComparer.Instance);
            Extension[] removed;
            int firstIndex;
            bool contiguous;
            lock (_gate)
            {
                var removedIndices = new List<int>();
                var removedItems = new List<Extension>();
                for (int index = 0; index < _items.Count; index++)
                {
                    if (removedSet.Contains(_items[index]))
                    {
                        removedIndices.Add(index);
                        removedItems.Add(_items[index]);
                    }
                }

                if (removedItems.Count == 0)
                    return;

                firstIndex = removedIndices[0];
                contiguous = removedIndices
                    .Select((index, offset) => index == firstIndex + offset)
                    .All(result => result);
                _items.RemoveAll(removedSet.Contains);
                removed = removedItems.ToArray();
            }

            NotifyCollectionChangedEventArgs args = contiguous
                ? new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Remove,
                    (IList)removed,
                    firstIndex)
                : new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset);
            NotifyObservers(args);
        }

        private void NotifyObservers(NotifyCollectionChangedEventArgs collectionArgs)
        {
            List<Exception>? failures = null;
            NotifyPropertyChanged(s_indexerChanged, ref failures);
            NotifyCollectionChanged(collectionArgs, ref failures);
            NotifyPropertyChanged(s_countChanged, ref failures);
            if (failures is not null)
                throw new AggregateException("One or more extension collection observers failed.", failures);
        }

        private void NotifyPropertyChanged(
            PropertyChangedEventArgs args,
            ref List<Exception>? failures)
        {
            if (PropertyChanged is not { } observers)
                return;

            foreach (PropertyChangedEventHandler observer in observers.GetInvocationList())
            {
                try
                {
                    observer(this, args);
                }
                catch (Exception ex)
                {
                    (failures ??= []).Add(ex);
                }
            }
        }

        private void NotifyCollectionChanged(
            NotifyCollectionChangedEventArgs args,
            ref List<Exception>? failures)
        {
            if (CollectionChanged is not { } observers)
                return;

            foreach (NotifyCollectionChangedEventHandler observer in observers.GetInvocationList())
            {
                try
                {
                    observer(this, args);
                }
                catch (Exception ex)
                {
                    (failures ??= []).Add(ex);
                }
            }
        }
    }
}
