using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditorNext.ProjectSystem;

namespace BEditorNext.ViewModels;

public class TimelineViewModel
{
    public TimelineViewModel(Scene scene)
    {
        Scene = scene;
    }

    public Scene Scene { get; }
}
