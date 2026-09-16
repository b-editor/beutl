using System.Runtime.CompilerServices;

namespace Beutl.Serialization;

/// <summary>
/// Keeps persisted-content migration requirements reported by values that have nowhere to keep one
/// themselves, so that assigning such a value to another owner later still raises that owner's
/// minimum application version.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="CoreObject"/> retains its own requirement, and a nested value hands its report to
/// the parent context while the surrounding graph is still being deserialized. A value that is
/// neither — a plain <see cref="ICoreSerializable"/> deserialized on its own — has no field to put
/// the report in and no parent to report to, so the requirement used to be dropped the moment
/// deserialization ended.
/// </para>
/// <para>
/// Requirements are keyed weakly on the value itself, so one lives exactly as long as the migrated
/// value it describes and never keeps that value alive.
/// </para>
/// </remarks>
internal static class AttachedContentMigrations
{
    private static readonly ConditionalWeakTable<ICoreSerializable, Requirement> s_requirements = new();
    private static volatile bool s_any;

    /// <summary>
    /// Gets a value indicating whether nothing has been retained yet, so that callers can skip the
    /// lookup on paths that run for every serialized value.
    /// </summary>
    /// <remarks>
    /// Never reset once a requirement has been retained: entries disappear with their values and no
    /// observation of the table is race-free, while the only cost of staying <see langword="false"/>
    /// is the lookup this exists to avoid.
    /// </remarks>
    internal static bool IsEmpty => !s_any;

    /// <summary>
    /// Gets a value indicating whether a live requirement still has to reach an owner, which is what
    /// a save has to discover before it writes anything.
    /// </summary>
    /// <remarks>
    /// This is the gate for work proportional to the graph, so unlike <see cref="IsEmpty"/> it goes
    /// back to <see langword="false"/>: the entries of collected values are gone, and a requirement
    /// an owner has taken over is found by the ordinary walk from then on. The table holds one entry
    /// per migrated standalone value, so the scan costs nothing measurable beside a save.
    /// </remarks>
    internal static bool HasPendingTransfer
    {
        get
        {
            if (!s_any)
            {
                return false;
            }

            foreach (KeyValuePair<ICoreSerializable, Requirement> entry in s_requirements)
            {
                if (!entry.Value.IsTransferred)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Retains <paramref name="minAppVersion"/> for <paramref name="value"/>, keeping the higher of
    /// it and any version already retained.
    /// </summary>
    internal static void Merge(ICoreSerializable value, string minAppVersion)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(minAppVersion);

        // A boxed value type has no identity to attach to: the copy that reaches the new owner is a
        // different object, so an entry here could never be found again. Such a value still reports
        // through its parent context while the graph around it is being deserialized.
        if (value.GetType().IsValueType)
        {
            return;
        }

        s_any = true;
        s_requirements.GetOrCreateValue(value).Merge(minAppVersion);
    }

    /// <summary>
    /// Returns the minimum application version retained for <paramref name="value"/>, or
    /// <see langword="null"/> when it reported no migration.
    /// </summary>
    internal static string? Get(ICoreSerializable value)
    {
        return s_requirements.TryGetValue(value, out Requirement? requirement)
            ? requirement.MinAppVersion
            : null;
    }

    /// <summary>
    /// Records that an owner has taken <paramref name="value"/>'s requirement over and keeps it from
    /// here on, so no later save has to discover it again.
    /// </summary>
    internal static void MarkTransferred(ICoreSerializable value)
    {
        if (s_requirements.TryGetValue(value, out Requirement? requirement))
        {
            requirement.MarkTransferred();
        }
    }

    /// <summary>
    /// Returns whether <paramref name="value"/> still carries a requirement no owner has taken over.
    /// </summary>
    internal static bool IsPendingTransfer(ICoreSerializable value)
    {
        return s_requirements.TryGetValue(value, out Requirement? requirement)
               && !requirement.IsTransferred;
    }

    private sealed class Requirement
    {
        private volatile bool _transferred;
        private string? _minAppVersion;

        internal string? MinAppVersion => Volatile.Read(ref _minAppVersion);

        internal bool IsTransferred => _transferred;

        internal void MarkTransferred()
        {
            _transferred = true;
        }

        internal void Merge(string minAppVersion)
        {
            while (true)
            {
                string? current = Volatile.Read(ref _minAppVersion);
                string? required = Project.GetMaximumMigrationVersion(current, minAppVersion);
                if (ReferenceEquals(required, current))
                {
                    // Already covered by what the owners were given.
                    return;
                }

                if (ReferenceEquals(
                        Interlocked.CompareExchange(ref _minAppVersion, required, current),
                        current))
                {
                    // A raised requirement has to reach the owners again.
                    _transferred = false;
                    return;
                }
            }
        }
    }
}
