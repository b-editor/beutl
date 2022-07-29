using BeUtl.Media;

using Reactive.Bindings;

namespace BeUtl.ViewModels.Dialogs;

public sealed class PickFontFamilyViewModel
{
    public ReactivePropertySlim<FontFamily> SelectedItem { get; } = new(FontFamily.Default);
}
