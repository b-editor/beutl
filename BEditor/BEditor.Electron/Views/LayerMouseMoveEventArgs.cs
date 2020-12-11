using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Components.Web;

namespace BEditor.Views
{
    public class LayerMouseEventArgs : EventArgs
    {
        public MouseEventArgs Base { get; init; }
        public int Layer { get; init; }
    }
}
