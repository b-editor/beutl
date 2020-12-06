using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using BEditor.Core.Data;

namespace BEditor.Views.Timelines
{
    public partial class Timeline
    {
        protected override void OnInitialized()
        {
            base.OnInitialized();
            
        }

        public Scene Scene { get; set; }
    }
}
