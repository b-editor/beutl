using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditorNext.ProjectSystem;

namespace BEditorNext.ViewModels;

public class EditViewModel
{
    public EditViewModel(Scene scene)
    {
        Scene = scene;
        Timeline = new TimelineViewModel(scene);
    }

    public Scene Scene { get; set; }

    public TimelineViewModel Timeline { get; }
}
