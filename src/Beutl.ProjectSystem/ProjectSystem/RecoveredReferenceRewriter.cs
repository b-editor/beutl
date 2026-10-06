using System.Collections;
using System.Reflection;
using Beutl.Utilities;

namespace Beutl.ProjectSystem;

// Rewrites references that still name the Id a recovered object had before reconciliation reassigned it.
// Values are memoized by identity, so aliases and cycles resolve to one replacement for the whole pass.
internal sealed class RecoveredReferenceRewriter(
    IReadOnlyDictionary<Guid, Guid> elementIdMigrations,
    IReadOnlyDictionary<Guid, Guid> descendantIdMigrations,
    IReadOnlyDictionary<Guid, CoreObject> referenceTargets)
{
    private readonly HashSet<object> _active = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, RecoveredReferenceRewriteEntry> _memo = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<RecoveredReferenceRewriteEntry> _activeRewritables = new();
    private int _rewriteCount;

    public bool TryGetMigratedId(Guid originalId, out Guid migratedId)
    {
        return elementIdMigrations.TryGetValue(originalId, out migratedId)
               || descendantIdMigrations.TryGetValue(originalId, out migratedId);
    }

    private object ResolveMigratedReference(
        IReference reference,
        Guid migratedId)
    {
        if (referenceTargets.TryGetValue(migratedId, out CoreObject? target)
            && reference.ObjectType.IsInstanceOfType(target))
        {
            return reference.Resolved(target);
        }

        return reference;
    }

    public object? Rewrite(object? value)
    {
        if (value is IReference reference)
        {
            return RewriteReference(value, reference);
        }

        if (value is IOptional { HasValue: true } optional)
        {
            return RewriteOptional(value, optional);
        }

        if (value is null or string)
        {
            return value;
        }

        return value.GetType().IsValueType
            ? RewriteContents(value)
            : RewriteTracked(value);
    }

    private object RewriteReference(object value, IReference reference)
    {
        object rewritten = TryGetMigratedId(reference.Id, out Guid migratedId)
            ? ResolveMigratedReference(reference, migratedId)
            : value;
        if (HasReferenceRewrite(value, rewritten))
        {
            RecordRewrite();
        }

        return rewritten;
    }

    private object RewriteOptional(object value, IOptional optional)
    {
        object? item = optional.ToObject().Value;
        object? migratedItem = Rewrite(item);
        if (HasReferenceRewrite(item, migratedItem))
        {
            try
            {
                ConstructorInfo? constructor = value.GetType().GetConstructor([optional.GetValueType()]);
                return constructor?.Invoke([migratedItem]) ?? value;
            }
            catch (Exception ex) when (!ExceptionHelpers.ContainsFatalFailure(ex)
                                      && ex is TargetInvocationException
                                               or ArgumentException
                                               or MemberAccessException)
            {
                return value;
            }
        }

        return value;
    }

    private object? RewriteTracked(object value)
    {
        if (_memo.TryGetValue(value, out RecoveredReferenceRewriteEntry? cached))
        {
            if (_activeRewritables.TryPeek(out RecoveredReferenceRewriteEntry? parent))
            {
                parent.Dependencies.Add(cached);
            }

            return cached.ShouldUseTargetDuringTraversal()
                ? cached.Target
                : cached.Source;
        }

        if (!_active.Add(value))
        {
            return value;
        }

        try
        {
            object? rewrittenValue = RewriteContents(value);
            if (!_memo.ContainsKey(value))
            {
                _memo[value] = new RecoveredReferenceRewriteEntry(value, rewrittenValue)
                {
                    Complete = true,
                    DirectChanged = HasReferenceRewrite(value, rewrittenValue),
                };
            }

            return rewrittenValue;
        }
        finally
        {
            _active.Remove(value);
        }
    }

    private object? RewriteContents(object value)
    {
        if (value is IReferenceRewritable rewritable)
        {
            return RewriteRewritable(value, rewritable);
        }

        if (value is IDictionary dictionary)
        {
            return RewriteDictionary(dictionary);
        }

        if (value is IList list)
        {
            return RewriteList(list);
        }

        if (value is IEnumerable enumerable)
        {
            return RewriteEnumerable(enumerable);
        }

        return value;
    }

    private object? RewriteRewritable(object value, IReferenceRewritable rewritable)
    {
        object? rewrittenValue = value;
        IReferenceRewritable target = rewritable.CreateReferenceRewriteTarget();
        if (target is not null && target.GetType() == value.GetType())
        {
            var entry = new RecoveredReferenceRewriteEntry(value, target);
            if (_activeRewritables.TryPeek(out RecoveredReferenceRewriteEntry? parent))
            {
                parent.Dependencies.Add(entry);
            }

            _memo[value] = entry;
            _activeRewritables.Push(entry);
            try
            {
                target.RewriteReferences(new RecoveredReferenceRewriteContext(this));
            }
            finally
            {
                _activeRewritables.Pop();
            }

            entry.Complete = true;
            if (entry.ShouldUseTargetDuringTraversal())
            {
                rewrittenValue = target;
            }
        }

        return rewrittenValue;
    }

    private object? RewriteDictionary(IDictionary dictionary)
    {
        object? rewrittenValue = dictionary;
        int rewriteCount = _rewriteCount;
        var entries = new List<DictionaryEntry>();
        bool changed = false;
        foreach (object key in dictionary.Keys.Cast<object>().ToArray())
        {
            object? item = dictionary[key];
            object? migratedItem = Rewrite(item);
            entries.Add(new DictionaryEntry(key, migratedItem));
            changed |= HasReferenceRewrite(item, migratedItem);
        }

        if (changed)
        {
            if (!dictionary.IsReadOnly)
            {
                foreach (DictionaryEntry entry in entries) dictionary[entry.Key] = entry.Value;
            }
            else if (RecoveredCollectionFactory.RebuildDictionary(dictionary, entries.ToArray()) is { } rebuilt)
                rewrittenValue = rebuilt;
            else
                _rewriteCount = rewriteCount;
        }

        return rewrittenValue;
    }

    private object? RewriteList(IList list)
    {
        object? rewrittenValue = list;
        int rewriteCount = _rewriteCount;
        object?[] rewrittenItems = new object?[list.Count];
        bool hasRewrittenItem = false;
        for (int i = 0; i < list.Count; i++)
        {
            object? item = list[i];
            object? migratedItem = Rewrite(item);
            rewrittenItems[i] = migratedItem;
            hasRewrittenItem |= HasReferenceRewrite(item, migratedItem);
        }

        if (hasRewrittenItem)
        {
            if (!list.IsReadOnly)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    list[i] = rewrittenItems[i];
                }
            }
            else if ((RecoveredCollectionFactory.RebuildEnumerable(list, rewrittenItems)
                      ?? RecoveredCollectionFactory.RebuildReadOnlyList(list, rewrittenItems)) is { } rebuilt)
            {
                rewrittenValue = rebuilt;
            }
            else
            {
                _rewriteCount = rewriteCount;
            }
        }

        return rewrittenValue;
    }

    private object? RewriteEnumerable(IEnumerable enumerable)
    {
        object? rewrittenValue = enumerable;
        int rewriteCount = _rewriteCount;
        object?[] items = enumerable.Cast<object?>().ToArray();
        object?[] rewrittenItems = items.Select(item => Rewrite(item)).ToArray();
        if (items.Where((item, index) => HasReferenceRewrite(item, rewrittenItems[index])).Any())
        {
            if (RecoveredCollectionFactory.RebuildEnumerable(enumerable, rewrittenItems) is { } rebuilt)
                rewrittenValue = rebuilt;
            else
                _rewriteCount = rewriteCount;
        }

        return rewrittenValue;
    }

    private void RecordRewrite()
    {
        _rewriteCount++;
        if (_activeRewritables.TryPeek(out RecoveredReferenceRewriteEntry? entry))
        {
            entry.DirectChanged = true;
        }
    }

    public static bool HasReferenceRewrite(object? current, object? rewritten)
    {
        if (ReferenceEquals(current, rewritten))
        {
            return false;
        }

        if (current is null || rewritten is null)
        {
            return true;
        }

        return current.GetType().IsValueType
            ? !Equals(current, rewritten)
            : true;
    }

    private sealed class RecoveredReferenceRewriteContext(
        RecoveredReferenceRewriter rewriter) : IReferenceRewriteContext
    {
        public T Rewrite<T>(T value)
        {
            object? rewritten = rewriter.Rewrite(value);
            return rewritten is T typed ? typed : value;
        }
    }

    private sealed class RecoveredReferenceRewriteEntry(object source, object? target)
    {
        public object Source { get; } = source;

        public object? Target { get; } = target;

        public HashSet<RecoveredReferenceRewriteEntry> Dependencies { get; }
            = new(ReferenceEqualityComparer.Instance);

        public bool DirectChanged { get; set; }

        public bool Complete { get; set; }

        public bool ShouldUseTargetDuringTraversal()
        {
            return !Complete
                   || Dependencies.Any(static dependency => !dependency.Complete)
                   || IsChanged(new HashSet<RecoveredReferenceRewriteEntry>(ReferenceEqualityComparer.Instance));
        }

        private bool IsChanged(ISet<RecoveredReferenceRewriteEntry> visited)
        {
            if (DirectChanged)
            {
                return true;
            }

            return visited.Add(this)
                   && Dependencies.Any(dependency => dependency.IsChanged(visited));
        }
    }
}
