using BEditorNext.ProjectSystem;

namespace BEditorNext.ViewModels.Editors;

public sealed class PropertiesEditorViewModel
{
    public PropertiesEditorViewModel(SceneLayer layer)
    {
        Layer = layer;
    }

    public SceneLayer Layer { get; }
}
