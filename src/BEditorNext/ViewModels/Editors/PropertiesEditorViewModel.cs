using BEditorNext.ProjectSystem;

namespace BEditorNext.ViewModels.Editors;

public sealed class PropertiesEditorViewModel
{
    public PropertiesEditorViewModel(Layer layer)
    {
        Layer = layer;
    }

    public Layer Layer { get; }
}
