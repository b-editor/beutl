namespace BeUtl.Framework;

public abstract class LocalizedPropertyNameExtension : Extension
{
    public abstract IObservable<string>? GetLocalizedName(CoreProperty property);
}
