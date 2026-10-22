using Beutl.Media;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class FontFamilyEditorViewModel(IPropertyAdapter<FontFamily?> property) : ValueEditorViewModel<FontFamily?>(property);
