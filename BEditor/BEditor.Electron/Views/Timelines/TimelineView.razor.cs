using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using BEditor.Core.Data;
using BEditor.Models;

using Microsoft.AspNetCore.Components;

namespace BEditor.Views.Timelines
{
    public partial class TimelineView
    {
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

        public Scene Scene => AppData.Current.Project.Value.PreviewScene;
    }
}
