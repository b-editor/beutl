using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data;
using BEditor.Core.Data.EffectData;
using BEditor.Core.Data.ObjectData;
using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;

namespace BEditor.Core.Extensions
{
    public static class ExtensionProperty
    {
        public static EffectElement GetParent(this IChild<EffectElement> context) => context.Parent;
        public static ClipData GetClipData(this IChild<EffectElement> context) => context.Parent.Parent;
        public static Scene GetScene(this IChild<EffectElement> context) => context.Parent.Parent.Parent;
        public static Project GetProject(this IChild<EffectElement> context) => context.Parent.Parent.Parent.Parent;

        public static ClipData GetParent(this IChild<ClipData> context) => context.Parent; 
        public static Scene GetScene(this IChild<ClipData> context) => context.Parent.Parent;
        public static Project GetProject(this IChild<ClipData> context) => context.Parent.Parent.Parent;
        
        public static Scene GetParent(this IChild<Scene> context) => context.Parent;
        public static Project GetProject(this IChild<Scene> context) => context.Parent.Parent;
    }
}
