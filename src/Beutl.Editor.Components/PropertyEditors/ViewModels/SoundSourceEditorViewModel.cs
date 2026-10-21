using Beutl.Media.Source;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class SoundSourceEditorViewModel(IPropertyAdapter<SoundSource?> property)
    : LocalFileSourceEditorViewModel<SoundSource>(property);
