using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using BEditor.Core.Data;
using BEditor.Models;

using Microsoft.AspNetCore.Components;

namespace BEditor.Views.Timelines
{
    public partial class LayerView
    {
        [Parameter]
        public int Layer { get; set; }
        public Scene Scene => AppData.Current.Project.Value.PreviewScene;
    }
}
