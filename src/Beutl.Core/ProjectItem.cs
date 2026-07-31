namespace Beutl;

public abstract class ProjectItem : Hierarchical
{
    internal virtual bool HasMigratedPersistedContent => false;
}
