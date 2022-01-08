namespace BEditorNext;

[Flags]
public enum PropertyObservability
{
    None = 0,
    Changed = 1,
    Changing = 2,
    ChangingAndChanged = Changed | Changing
}
