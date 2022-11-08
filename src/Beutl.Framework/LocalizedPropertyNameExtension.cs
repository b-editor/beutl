namespace Beutl.Framework;

public abstract class LocalizedPropertyNameExtension : Extension
{
    public abstract string? GetLocalizedName(CoreProperty property);
}
