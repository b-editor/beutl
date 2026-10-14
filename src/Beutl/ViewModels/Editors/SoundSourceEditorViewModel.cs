using Beutl.Media.Source;

namespace Beutl.ViewModels.Editors;

public sealed class SoundSourceEditorViewModel(IPropertyAdapter<SoundSource?> property)
    : LocalFileSourceEditorViewModel<SoundSource>(property);
