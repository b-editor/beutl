using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor;

public sealed partial class HistoryManager
{
    private void TruncateEntriesAfter(int lastKeptIndex)
    {
        for (int i = _entries.Count - 1; i > lastKeptIndex; i--)
        {
            RemoveEntryAt(i);
        }
    }

    private void AddEntry(HistoryEntry entry)
    {
        int index = _entries.Count;
        EntrySubscriber[] subscribers = _entrySubscribers.ToArray();
        _entryPublicationDepth++;
        try
        {
            try
            {
                _entries.Add(entry);
            }
            catch (Exception ex) when (_entries.Count == index + 1
                && ReferenceEquals(_entries[index], entry))
            {
                _logger.LogError(ex, "A direct history entry observer failed while adding entry {EntryIndex}.", index);
            }

            NotifyEntrySubscribersSafely(
                new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, entry, index),
                subscribers);
        }
        finally
        {
            _entryPublicationDepth--;
        }
    }

    private void ReplaceEntry(int index, HistoryEntry entry)
    {
        HistoryEntry previous = _entries[index];
        EntrySubscriber[] subscribers = _entrySubscribers.ToArray();
        _entryPublicationDepth++;
        try
        {
            try
            {
                _entries[index] = entry;
            }
            catch (Exception ex) when (ReferenceEquals(_entries[index], entry))
            {
                _logger.LogError(ex, "A direct history entry observer failed while replacing entry {EntryIndex}.", index);
            }

            NotifyEntrySubscribersSafely(
                new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Replace,
                    entry,
                    previous,
                    index),
                subscribers);
        }
        finally
        {
            _entryPublicationDepth--;
        }
    }

    private void RemoveEntryAt(int index)
    {
        HistoryEntry removed = _entries[index];
        int previousCount = _entries.Count;
        EntrySubscriber[] subscribers = _entrySubscribers.ToArray();
        _entryPublicationDepth++;
        try
        {
            try
            {
                _entries.RemoveAt(index);
            }
            catch (Exception ex) when (_entries.Count == previousCount - 1)
            {
                _logger.LogError(ex, "A direct history entry observer failed while removing entry {EntryIndex}.", index);
            }

            NotifyEntrySubscribersSafely(
                new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removed, index),
                subscribers);
        }
        finally
        {
            _entryPublicationDepth--;
        }
    }

    private void NotifyEntrySubscribersSafely(
        NotifyCollectionChangedEventArgs args,
        IReadOnlyList<EntrySubscriber> subscribers)
    {
        foreach (EntrySubscriber subscriber in subscribers)
        {
            if (!_entrySubscribers.Contains(subscriber))
                continue;
            try
            {
                subscriber.Handler(_readOnlyEntries, args);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "A history entry subscriber failed while handling {Action}; continuing publication.",
                    args.Action);
            }
        }
    }

    private sealed class EntrySubscriber(NotifyCollectionChangedEventHandler handler)
    {
        public NotifyCollectionChangedEventHandler Handler { get; } = handler;
    }

    private sealed class IsolatedEntryCollection(
        ObservableCollection<HistoryEntry> source,
        ILogger logger) : ReadOnlyObservableCollection<HistoryEntry>(source),
        INotifyCollectionChanged, INotifyPropertyChanged
    {
        private event NotifyCollectionChangedEventHandler? CollectionHandlers;
        private event PropertyChangedEventHandler? PropertyHandlers;

        event NotifyCollectionChangedEventHandler? INotifyCollectionChanged.CollectionChanged
        {
            add => CollectionHandlers += value;
            remove => CollectionHandlers -= value;
        }

        event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
        {
            add => PropertyHandlers += value;
            remove => PropertyHandlers -= value;
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs args)
        {
            try { base.OnCollectionChanged(args); }
            catch (Exception ex) { Report(ex); }
            foreach (NotifyCollectionChangedEventHandler handler in
                     CollectionHandlers?.GetInvocationList() ?? [])
            {
                try { handler(this, args); }
                catch (Exception ex) { Report(ex); }
            }
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs args)
        {
            try { base.OnPropertyChanged(args); }
            catch (Exception ex) { Report(ex); }
            foreach (PropertyChangedEventHandler handler in PropertyHandlers?.GetInvocationList() ?? [])
            {
                try { handler(this, args); }
                catch (Exception ex) { Report(ex); }
            }
        }

        private void Report(Exception exception)
            => logger.LogError(exception, "A direct history entry observer failed; continuing publication.");
    }
}
