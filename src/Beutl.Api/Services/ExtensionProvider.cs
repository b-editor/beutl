using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Beutl.Collections;
using Beutl.Configuration;
using Beutl.Extensibility;

using static Beutl.Configuration.ExtensionConfig;

namespace Beutl.Api.Services;

public sealed class ExtensionProvider : IExtensionRegistry
{
    private readonly Dictionary<int, ExtensionEntry[]> _entriesByPackage = [];
    private readonly Dictionary<ExtensionId, ExtensionEntry> _entriesById = [];
    private readonly ExtensionConfig _config = GlobalConfiguration.Instance.ExtensionConfig;
    private readonly Dictionary<Type, Array> _cache = [];
    private readonly ExtensionCollection _extensions = new();
    private readonly object _lock = new();
    private readonly object _mutationGate = new();
    private Extension[] _snapshot = [];
    private ExtensionEntry[] _entrySnapshot = [];

    public ExtensionProvider()
    {
    }

    public event EventHandler? ExtensionsChanged;

    public IReadOnlyList<ExtensionDescriptor> Extensions
    {
        get
        {
            lock (_lock)
            {
                return Array.AsReadOnly(_entrySnapshot
                    .Select(entry => entry.Descriptor)
                    .ToArray());
            }
        }
    }

    public IReadOnlyList<ExtensionDescriptor> GetDescriptors<TExtension>()
        where TExtension : Extension
    {
        lock (_lock)
        {
            return Array.AsReadOnly(_entrySnapshot
                .Where(entry => entry.Extension is TExtension)
                .Select(entry => entry.Descriptor)
                .ToArray());
        }
    }

    public bool TryAcquire<TExtension>(
        ExtensionId id,
        [NotNullWhen(true)] out IExtensionLease<TExtension>? lease)
        where TExtension : Extension
    {
        lock (_lock)
        {
            if (_entriesById.TryGetValue(id, out ExtensionEntry? entry))
                return entry.TryAcquire(out lease);

            lease = null;
            return false;
        }
    }

    internal ICoreReadOnlyList<Extension> AllExtensions => _extensions;

    internal TExtension[] GetExtensions<TExtension>()
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

    internal IReadOnlyList<Extension> GetPackageExtensions(int packageId)
    {
        lock (_lock)
        {
            return _entriesByPackage.TryGetValue(packageId, out ExtensionEntry[]? entries)
                ? entries.Select(entry => entry.Extension).ToArray()
                : [];
        }
    }

    internal EditorExtension? MatchEditorExtension(string file)
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

    internal ProjectItemExtension? MatchProjectItemExtension(string file)
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

    internal IEnumerable<ProjectItemExtension> MatchProjectItemExtensions(string file)
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

    internal void AddExtensions(int packageId, IReadOnlyList<Extension> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        Extension[] ownedExtensions = extensions.ToArray();
        if (ownedExtensions.Any(extension => extension is null))
            throw new ArgumentException("Extensions cannot contain null.", nameof(extensions));
        ExtensionEntry[] ownedEntries = ownedExtensions
            .Select(extension => new ExtensionEntry(extension))
            .ToArray();

        lock (_mutationGate)
        {
            lock (_lock)
            {
                if (_entriesByPackage.ContainsKey(packageId))
                {
                    throw new InvalidOperationException(
                        $"Extensions for package (id: {packageId}) are already registered.");
                }
                if (ownedEntries
                    .Select(entry => entry.Descriptor.Id)
                    .Distinct()
                    .Count() != ownedEntries.Length
                    || ownedEntries.Any(entry => _entriesById.ContainsKey(entry.Descriptor.Id)))
                {
                    throw new InvalidOperationException("An extension identifier is already registered.");
                }

                // Internal composition observes executable instances first. Public metadata and
                // acquisition remain unchanged until every internal observer accepts the batch.
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
                    var ownedSet = new HashSet<Extension>(
                        ownedExtensions,
                        ReferenceEqualityComparer.Instance);
                    _snapshot = _snapshot
                        .Where(extension => !ownedSet.Contains(extension))
                        .ToArray();
                    _cache.Clear();
                }
                Task[] providerLeaseDrains = ownedEntries
                    .Select(entry => entry.RetireAsync())
                    .ToArray();
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
                    new ExtensionRemoval(ownedExtensions, providerLeaseDrains),
                    registrationFailure);
            }

            lock (_lock)
            {
                _entriesByPackage.Add(packageId, ownedEntries);
                foreach (ExtensionEntry entry in ownedEntries)
                {
                    _entriesById.Add(entry.Descriptor.Id, entry);
                }
                _entrySnapshot = [.. _entrySnapshot, .. ownedEntries];
            }

            if (ownedExtensions.Length > 0)
            {
                NotifyExtensionsChanged();
            }
        }
    }

    internal ExtensionRemoval RemoveExtensions(int packageId)
    {
        lock (_mutationGate)
        {
            Extension[] extensions;
            ExtensionEntry[] entries;
            Task[] providerLeaseDrains;
            lock (_lock)
            {
                if (!_entriesByPackage.Remove(packageId, out ExtensionEntry[]? removed))
                {
                    return new ExtensionRemoval([]);
                }

                entries = removed;
                extensions = entries.Select(entry => entry.Extension).ToArray();
                foreach (ExtensionEntry entry in entries)
                {
                    _entriesById.Remove(entry.Descriptor.Id);
                }
                var removedSet = new HashSet<Extension>(extensions, ReferenceEqualityComparer.Instance);
                _snapshot = _snapshot.Where(extension => !removedSet.Contains(extension)).ToArray();
                var removedEntrySet = new HashSet<ExtensionEntry>(
                    entries,
                    ReferenceEqualityComparer.Instance);
                _entrySnapshot = _entrySnapshot
                    .Where(entry => !removedEntrySet.Contains(entry))
                    .ToArray();
                _cache.Clear();
                providerLeaseDrains = entries.Select(entry => entry.RetireAsync()).ToArray();
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
            if (extensions.Length > 0)
            {
                NotifyExtensionsChanged();
            }
            ExtensionRemoval removal = new(extensions, providerLeaseDrains);
            if (notificationFailure is not null)
                throw new ExtensionRemovalNotificationException(removal, notificationFailure);

            return removal;
        }
    }

    internal void SynchronizeMutation(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_mutationGate)
        {
            action();
        }
    }

    ICoreReadOnlyList<Extension> IExtensionRegistry.AllExtensions => AllExtensions;

    TExtension[] IExtensionRegistry.GetExtensions<TExtension>() => GetExtensions<TExtension>();

    EditorExtension? IExtensionRegistry.MatchEditorExtension(string file) => MatchEditorExtension(file);

    void IExtensionRegistry.AddExtensions(int packageId, IReadOnlyList<Extension> extensions)
        => AddExtensions(packageId, extensions);

    IReadOnlyList<Extension> IExtensionRegistry.GetPackageExtensions(int packageId)
        => GetPackageExtensions(packageId);

    ExtensionRemoval IExtensionRegistry.RemoveExtensions(int packageId)
        => RemoveExtensions(packageId);

    void IExtensionRegistry.SynchronizeMutation(Action action) => SynchronizeMutation(action);

    private void NotifyExtensionsChanged()
    {
        if (ExtensionsChanged is not { } observers)
            return;

        foreach (EventHandler observer in observers.GetInvocationList())
        {
            try
            {
                observer(this, EventArgs.Empty);
            }
            catch
            {
                // Public observers cannot veto or roll back a package mutation. Internal
                // lifetime-sensitive composition runs through AllExtensions first.
            }
        }
    }

    private sealed class ExtensionEntry
    {
        private readonly object _gate = new();
        private TaskCompletionSource? _drained;
        private int _activeLeases;
        private bool _retired;

        public ExtensionEntry(Extension extension)
        {
            Extension = extension;
            Type extensionType = extension.GetType();
            Descriptor = new ExtensionDescriptor(
                new ExtensionId(Guid.NewGuid()),
                extensionType.AssemblyQualifiedName
                    ?? extensionType.FullName
                    ?? extensionType.Name);
        }

        public Extension Extension { get; }

        public ExtensionDescriptor Descriptor { get; }

        public bool TryAcquire<TExtension>(
            [NotNullWhen(true)] out IExtensionLease<TExtension>? lease)
            where TExtension : Extension
        {
            lock (_gate)
            {
                if (_retired || Extension is not TExtension typed)
                {
                    lease = null;
                    return false;
                }

                _activeLeases++;
                lease = new ExtensionLease<TExtension>(this, typed);
                return true;
            }
        }

        public Task RetireAsync()
        {
            lock (_gate)
            {
                _retired = true;
                return _activeLeases == 0
                    ? Task.CompletedTask
                    : (_drained ??= new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }
        }

        public void Release()
        {
            TaskCompletionSource? drained = null;
            lock (_gate)
            {
                _activeLeases--;
                if (_retired && _activeLeases == 0)
                    drained = _drained;
            }

            drained?.TrySetResult();
        }
    }

    private sealed class ExtensionLease<TExtension>(ExtensionEntry entry, TExtension extension)
        : IExtensionLease<TExtension>
        where TExtension : Extension
    {
        private ExtensionEntry? _entry = entry;
        private TExtension? _extension = extension;

        public TExtension Extension
            => Volatile.Read(ref _extension)
                ?? throw new ObjectDisposedException(nameof(IExtensionLease<TExtension>));

        public void Dispose()
        {
            Interlocked.Exchange(ref _extension, null);
            Interlocked.Exchange(ref _entry, null)?.Release();
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
