using Beutl.Media.Source;

namespace Beutl.ViewModels.Editors;

public sealed class VideoSourceEditorViewModel(IPropertyAdapter<VideoSource?> property)
    : LocalFileSourceEditorViewModel<VideoSource>(property);
