namespace Beutl.Serialization;

public interface ICoreSerializationContext
{
    CoreSerializationMode Mode { get; }

    Uri? BaseUri { get; }

    Type OwnerType { get; }

    /// <summary>
    /// Reports that deserialization migrated persisted content and records the minimum Beutl
    /// version required to read the migrated form.
    /// </summary>
    /// <param name="minAppVersion">A valid NuGet version string required by the migrated form.</param>
    /// <remarks>
    /// Call this from <see cref="ICoreSerializable.Deserialize"/> only when legacy persisted data
    /// was actually rewritten. Reports from nested serializable values are combined and propagated
    /// to their containing project.
    /// </remarks>
    void ReportPersistedContentMigration(string minAppVersion);

    void SetValue<T>(string name, T? value);

    T? GetValue<T>(string name);

    bool Contains(string name);

    void Populate(string name, ICoreSerializable obj);

    void Resolve(Guid id, Action<ICoreSerializable> callback);
}
