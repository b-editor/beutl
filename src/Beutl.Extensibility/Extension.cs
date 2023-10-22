namespace Beutl.Extensibility;

// 拡張機能の基本クラス
public abstract class Extension
{
    public abstract string Name { get; }

    public abstract string DisplayName { get; }

    public virtual ExtensionSettings? Settings { get; }

    public virtual void Load()
    {
    }
}
