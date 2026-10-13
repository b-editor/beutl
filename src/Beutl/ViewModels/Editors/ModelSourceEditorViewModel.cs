using Beutl.Graphics3D.Models;

namespace Beutl.ViewModels.Editors;

public sealed class ModelSourceEditorViewModel(IPropertyAdapter<ModelSource?> property)
    : LocalFileSourceEditorViewModel<ModelSource>(property);
