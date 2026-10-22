namespace Beutl.Collections;

// Replaces a list's items without its item type, for a value-only property that keeps the list.
internal interface IReplaceableList
{
    void ReplaceItems(object? source);
}
