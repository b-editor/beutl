using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditorNext.ProjectSystem;

namespace BEditorNext.ViewModels;

public sealed class AnimationTimelineViewModel
{
    public AnimationTimelineViewModel(SceneLayer layer, IAnimatableSetter setter)
    {
        Layer = layer;
        Setter = setter;
    }

    public SceneLayer Layer { get; }

    public IAnimatableSetter Setter { get; }
}
