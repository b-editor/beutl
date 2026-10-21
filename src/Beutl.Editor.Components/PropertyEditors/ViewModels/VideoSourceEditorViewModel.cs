using Beutl.Media.Source;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class VideoSourceEditorViewModel(IPropertyAdapter<VideoSource?> property)
    : LocalFileSourceEditorViewModel<VideoSource>(property);
