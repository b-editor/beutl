namespace Beutl;

public abstract class ProjectItem : Hierarchical
{
    // True when this item inherited legacy audio/video settings from its parent Project on load
    // and has not yet been written to its own file.
    internal bool HasUnpersistedLegacyRates { get; set; }
}
