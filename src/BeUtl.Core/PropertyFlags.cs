namespace BeUtl;

[Flags]
public enum PropertyFlags
{
    None = 0,
    Styleable = 1,
    Designable = 1 << 1,
    NotifyChanged = 1 << 2,
    Animatable = 1 << 3,
    All = Styleable | Designable | NotifyChanged | Animatable,
    [Obsolete]
    KnownFlags_1 = Styleable | Designable | NotifyChanged | Animatable,
}
