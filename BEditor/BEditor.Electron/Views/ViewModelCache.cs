using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using BEditor.Core.Data;
using BEditor.ViewModels.Timelines;

using Microsoft.AspNetCore.Components;

namespace BEditor.Views
{
    public static class Tool
    {
        public static ClipViewModel GetCreateClipViewModel(this ClipData clip)
        {
            const string key = "GetClipViewModel";
            if (!clip.ComponentData.ContainsKey(key))
            {
                clip.ComponentData.Add(key, new ClipViewModel(clip));
            }
            return clip.ComponentData[key];
        }
        public static TimelineViewModel GetCreateTimeLineViewModel(this Scene scene)
        {
            const string key = "GetTimeLineViewModel";
            if (!scene.ComponentData.ContainsKey(key))
            {
                scene.ComponentData.Add(key, new TimelineViewModel(scene));
            }
            return scene.ComponentData[key];
        }
        //public static RenderFragment CreatePropertyView(this EffectElement effect)
        //{

        //}
    }
}
