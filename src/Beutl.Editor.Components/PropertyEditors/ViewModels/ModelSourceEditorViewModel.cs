using Beutl.Graphics3D.Models;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class ModelSourceEditorViewModel(IPropertyAdapter<ModelSource?> property)
    : LocalFileSourceEditorViewModel<ModelSource>(property);
