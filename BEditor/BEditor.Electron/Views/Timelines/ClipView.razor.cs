using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using BEditor.Core.Data;
using BEditor.ViewModels.Timelines;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace BEditor.Views.Timelines
{
    public partial class ClipView
    {
        [Parameter]
        public ClipData Clip { get; set; }
        [Parameter]
        public double Margin_Left { get; set; }
        public ObjectElement Element => Clip.Effect[0] as ObjectElement;
        private ClipViewModel ViewModel => Clip.GetCreateClipViewModel();

        private void OnDragStart(DragEventArgs e)
        {
            Clip.Parent.GetCreateTimeLineViewModel().ClipDragStart(new((float)e.OffsetX, (float)e.OffsetY), ViewModel);
        }
        private void OnDragEnd(DragEventArgs e)
        {
            Clip.Parent.GetCreateTimeLineViewModel().ClipDragEnd();
        }
    }
}
