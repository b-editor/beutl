namespace Beutl.Extensibility;

// 拡張機能の基本クラス
public abstract class Extension
{
    public virtual string Name => GetType().Name;

    public virtual string DisplayName => TypeDisplayHelpers.GetLocalizedName(GetType());

    public virtual ExtensionSettings? Settings { get; }

    internal EventHandler? SettingsChangedHandler { get; set; }

    public virtual void Load()
    {
    }

    public virtual void Unload()
    {
    }
}
