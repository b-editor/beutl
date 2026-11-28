namespace Beutl.Editor.Components.PropertyEditors.LutLibrary;

// What a picker item stands for: the file to apply, and whether it can be read as a LUT.
internal sealed record LutLibraryPickerItem(string Path, bool IsError);
