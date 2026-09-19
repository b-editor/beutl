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
/// <para>
/// A value type is retained against the box it was deserialized into, which is the instance an
/// owner holding it through an interface keeps. A caller that unboxes and copies it leaves the
/// requirement behind, because the copy is a different value with no identity to find it by; such a
/// value still reports through its parent context while the graph around it is being deserialized.
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
    /// Gets the highest minimum application version any live value still carries, or
    /// <see langword="null"/> when none does.
    /// </summary>
    /// <remarks>
    /// A save compares this with the gate its project already carries to decide whether it has to
    /// discover anything before writing. Unlike <see cref="IsEmpty"/> this falls back to
    /// <see langword="null"/> as values are collected, and the table holds one entry per migrated
    /// standalone value, so the scan costs nothing measurable beside a save.
    /// </remarks>
    internal static string? HighestRetained
    {
        get
        {
            if (!s_any)
            {
                return null;
            }

            string? highest = null;
            foreach (KeyValuePair<ICoreSerializable, Requirement> entry in s_requirements)
            {
                highest = Project.GetMaximumMigrationVersion(highest, entry.Value.MinAppVersion);
            }

            return highest;
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

    private sealed class Requirement
    {
        private string? _minAppVersion;

        internal string? MinAppVersion => Volatile.Read(ref _minAppVersion);

        internal void Merge(string minAppVersion)
        {
            while (true)
            {
                string? current = Volatile.Read(ref _minAppVersion);
                string? required = Project.GetMaximumMigrationVersion(current, minAppVersion);
                if (ReferenceEquals(
                        Interlocked.CompareExchange(ref _minAppVersion, required, current),
                        current))
                {
                    return;
                }
            }
        }
    }
}
