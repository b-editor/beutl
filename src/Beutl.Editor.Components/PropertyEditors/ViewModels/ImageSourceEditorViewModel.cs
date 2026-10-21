using Beutl.Media.Source;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class ImageSourceEditorViewModel(IPropertyAdapter<ImageSource?> property)
    : LocalFileSourceEditorViewModel<ImageSource>(property);
