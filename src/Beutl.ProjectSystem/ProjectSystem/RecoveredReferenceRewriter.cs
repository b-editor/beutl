using System.Collections;
using System.Reflection;
using Beutl.Utilities;

namespace Beutl.ProjectSystem;

// Rewrites references that still name the Id a recovered object had before reconciliation reassigned it:
// IReference and IOptional values, and the items of lists and dictionaries that can be changed in place.
// Each collection is visited once per pass, so aliases and cycles end.
internal sealed class RecoveredReferenceRewriter(
    IReadOnlyDictionary<Guid, Guid> elementIdMigrations,
    IReadOnlyDictionary<Guid, Guid> descendantIdMigrations,
    IReadOnlyDictionary<Guid, CoreObject> referenceTargets)
{
    private readonly HashSet<object> _visited = new(ReferenceEqualityComparer.Instance);

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
        switch (value)
        {
            case IReference reference:
                return TryGetMigratedId(reference.Id, out Guid migratedId)
                    ? ResolveMigratedReference(reference, migratedId)
                    : value;
            case IOptional { HasValue: true } optional:
                return RewriteOptional(value, optional);
            case IDictionary { IsReadOnly: false } dictionary when _visited.Add(dictionary):
                RewriteDictionary(dictionary);
                return value;
            case IList { IsReadOnly: false } list when _visited.Add(list):
                RewriteList(list);
                return value;
            default:
                return value;
        }
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

    private void RewriteDictionary(IDictionary dictionary)
    {
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
            foreach (DictionaryEntry entry in entries) dictionary[entry.Key] = entry.Value;
        }
    }

    private void RewriteList(IList list)
    {
        object?[] rewrittenItems = new object?[list.Count];
        bool changed = false;
        for (int i = 0; i < list.Count; i++)
        {
            object? item = list[i];
            object? migratedItem = Rewrite(item);
            rewrittenItems[i] = migratedItem;
            changed |= HasReferenceRewrite(item, migratedItem);
        }

        if (changed)
        {
            for (int i = 0; i < list.Count; i++)
            {
                list[i] = rewrittenItems[i];
            }
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
}
