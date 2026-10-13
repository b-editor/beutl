using Beutl.Media.Source;

namespace Beutl.ViewModels.Editors;

public sealed class ImageSourceEditorViewModel(IPropertyAdapter<ImageSource?> property)
    : LocalFileSourceEditorViewModel<ImageSource>(property);
