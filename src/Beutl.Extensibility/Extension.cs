namespace Beutl.Extensibility;

// 拡張機能の基本クラス
public abstract class Extension
{
    public virtual string Name => GetType().Name;

    public virtual string DisplayName => TypeDisplayHelpers.GetLocalizedName(GetType());

    public virtual ExtensionSettings? Settings { get; }

    internal EventHandler? SettingsChangedHandler { get; set; }

    /// <summary>
    /// Called once when the extension is loaded. Override to perform initialization.
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
