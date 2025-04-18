using System.ComponentModel.DataAnnotations;

namespace Beutl.Extensibility;

// 拡張機能の基本クラス
public abstract class Extension
{
    private readonly Lazy<DisplayAttribute?> _displayAttribute;

    protected Extension()
    {
        _displayAttribute = new Lazy<DisplayAttribute?>(() =>
        {
            Type type = GetType();
            var displayAttribute = type.GetCustomAttributes(typeof(DisplayAttribute), false)
                .FirstOrDefault() as DisplayAttribute;
            return displayAttribute;
        });
    }

    public virtual string Name => GetType().Name;

    public virtual string DisplayName => _displayAttribute.Value?.GetName() ?? Name;

    public virtual ExtensionSettings? Settings { get; }

    public virtual void Load()
    {
    }
}
