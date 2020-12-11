using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data;
using BEditor.Models;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

using Reactive.Bindings;

namespace BEditor.Views.Timelines
{
    public partial class TimelineView
    {
        public Scene Scene => AppData.Current.Project.Value.PreviewScene;

        protected override void OnInitialized()
        {
            base.OnInitialized();

            AppData.Current.Project.Value.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(Project.PreviewScene))
                {
                    InvokeAsync(StateHasChanged);
                }
            };
        }
        private void TimelineMouseMove(MouseEventArgs e)
        {
            Scene.GetCreateTimeLineViewModel().TimelineMouseMove(new((float)e.OffsetX, (float)e.OffsetY));

            InvokeAsync(StateHasChanged);
        }
        private void TimelineMouseDown(MouseEventArgs e)
        {
            Scene.GetCreateTimeLineViewModel().TimelineMouseDown(new((float)e.OffsetX, (float)e.OffsetY));

            InvokeAsync(StateHasChanged);
        }
        private void TimelineMouseUp(MouseEventArgs e)
        {
            Scene.GetCreateTimeLineViewModel().TimelineMouseUp();
        }
        private void TimelineMouseOver(MouseEventArgs e)
        {

        }
        private void TimelineMouseOut(MouseEventArgs e)
        {

        }
        
        private void LayerMouseMove(LayerMouseEventArgs e)
        {
            Scene.GetCreateTimeLineViewModel().LayerMouseMove(e.Layer);
        }

        private void OnDrop(DragEventArgs e)
        {
            Scene.GetCreateTimeLineViewModel().ClipDrop(new((float)e.OffsetX, (float)e.OffsetY));

            InvokeAsync(StateHasChanged);
        }
    }
}
