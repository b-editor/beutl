namespace BeUtl;

[Flags]
public enum PropertyObservability
{
    None = 0b0000,
    Changed = 0b0001,
    DoNotNotifyLogicalTree = 0b0011,
}
