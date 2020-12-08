using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using BEditor.Core.Data;

using Microsoft.AspNetCore.Components;

namespace BEditor.Views.Timelines
{
    public partial class ClipView
    {
        [Parameter]
        public ClipData Clip { get; set; }
    }
}
