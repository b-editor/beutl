namespace Beutl.Editor.PropertyEditors;

// A host that shows one of several elements repeats each edit of its editors on the others, so that every element takes
// the edit as its own editor would: a value goes to the keyframe that element edits at its own playhead, and a command
// runs on its own values. Editors find it among their services.
internal interface IPropertyEditorMirror
{
    // Runs command on editor, then on an editor of the same properties of each other element, as one history entry; a
    // value shown while it is dragged takes none until it is set. The command gets how much later the element starts
    // than the one shown, so that a time on the shown element's clock falls on the same place of each. False while a
    // command runs, so that one inside it runs as it would alone.
    bool Run(
        PropertyEditorViewModel editor, Action<PropertyEditorViewModel, MediaTime> command, bool commit = true,
        string? commitName = null);

    // A value an editor writes, as another element takes it: a copy of its own in place of an object.
    object? CopyValue(object? value);

    // How the values the other elements have where the editor shows one differ from it, or null when they do not.
    IObservable<string?> ObserveDifference(PropertyEditorViewModel editor);
}
