using BEditorNext.Media;

using Reactive.Bindings;

namespace BEditorNext.ViewModels.Dialogs;

public sealed class PickFontFamilyViewModel
{
    public ReactivePropertySlim<FontFamily> SelectedItem { get; } = new(FontFamily.Default);

    public FontFamily[] Items { get; } = FontManager.Instance.FontFamilies.ToArray();
}
