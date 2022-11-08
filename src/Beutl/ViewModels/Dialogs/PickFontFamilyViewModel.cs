using Beutl.Media;

using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

public sealed class PickFontFamilyViewModel
{
    public ReactivePropertySlim<FontFamily> SelectedItem { get; } = new(FontFamily.Default);
}
