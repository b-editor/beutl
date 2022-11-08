namespace Beutl.Framework;

// 拡張機能の基本クラス
public abstract class Extension
{
    public abstract string Name { get; }

    public abstract string DisplayName { get; }

    public virtual void Load()
    {
    }
}
