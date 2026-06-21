namespace Beutl.Extensibility;

// Base class for extension entry points.
public abstract class Extension
{
    public virtual string Name => GetType().Name;

    public virtual string DisplayName => TypeDisplayHelpers.GetLocalizedName(GetType());

    public virtual ExtensionSettings? Settings { get; }

    /// <summary>
    /// Called once when the extension is loaded. Override to perform initialization.
    /// If this method throws, <see cref="Unload"/> is called to roll back any partial
    /// initialization, so write <see cref="Unload"/> to be idempotent and safe against
    /// partially initialized state.
    /// </summary>
    public virtual void Load()
    {
    }

    /// <summary>
    /// Called when the extension is unloaded. This may also be called to roll back a failed load —
    /// including after <see cref="Load"/> itself has thrown — so implementations must be idempotent
    /// and safe to run against partially initialized state.
    /// </summary>
    public virtual void Unload()
    {
    }
}
