using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
