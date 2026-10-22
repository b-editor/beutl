using Beutl.Media;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class AlignmentXEditorViewModel(IPropertyAdapter<AlignmentX> property) : ValueEditorViewModel<AlignmentX>(property);

public sealed class TextAlignmentEditorViewModel(IPropertyAdapter<TextAlignment> property) : ValueEditorViewModel<TextAlignment>(property);
