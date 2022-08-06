namespace BeUtl;

[Flags]
public enum PropertyFlags
{
    None = 0,
    Styleable = 1,
    Designable = 1 << 1,
    NotifyChanged = 1 << 2,
    Animatable = 1 << 3,
    KnownFlags_1 = Styleable | Designable | NotifyChanged | Animatable,
    All = Styleable | Designable | NotifyChanged | Animatable,
}
