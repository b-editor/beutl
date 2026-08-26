namespace Beutl;

public abstract class ProjectItem : Hierarchical
{
    /// <summary>
    /// Gets whether deserializing this item migrated persisted content to the current format.
    /// </summary>
    /// <remarks>
    /// Override this in project-item types that rewrite legacy data during deserialization so the
    /// containing project can update its application-version requirements.
    /// </remarks>
    protected internal virtual bool HasMigratedPersistedContent => false;
}
