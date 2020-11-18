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
        public static T GetParent<T>(this IChild<T> context) => context.Parent;
        public static T GetParent2<T>(this IChild<IChild<T>> context) => context.Parent.Parent;
        public static T GetParent3<T>(this IChild<IChild<IChild<T>>> context) => context.Parent.Parent.Parent;
        public static T GetParent4<T>(this IChild<IChild<IChild<IChild<T>>>> context) => context.Parent.Parent.Parent.Parent;
        public static T GetParent5<T>(this IChild<IChild<IChild<IChild<IChild<T>>>>> context) => context.Parent.Parent.Parent.Parent.Parent;
        public static T GetParent6<T>(this IChild<IChild<IChild<IChild<IChild<IChild<T>>>>>> context) => context.Parent.Parent.Parent.Parent.Parent.Parent;
        public static T GetParent7<T>(this IChild<IChild<IChild<IChild<IChild<IChild<IChild<T>>>>>>> context) => context.Parent.Parent.Parent.Parent.Parent.Parent.Parent;
        public static T GetParent8<T>(this IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<T>>>>>>>> context) => context.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent;
        public static T GetParent9<T>(this IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<T>>>>>>>>> context) => context.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent;
        public static T GetParent10<T>(this IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<T>>>>>>>>>> context) => context.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent;
        public static T GetParent11<T>(this IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<T>>>>>>>>>>> context) => context.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent;
        public static T GetParent12<T>(this IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<T>>>>>>>>>>>> context) => context.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent;
        public static T GetParent13<T>(this IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<T>>>>>>>>>>>>> context) => context.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent;
        public static T GetParent14<T>(this IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<T>>>>>>>>>>>>>> context) => context.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent;
        public static T GetParent15<T>(this IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<T>>>>>>>>>>>>>>> context) => context.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent;
        public static T GetParent16<T>(this IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<IChild<T>>>>>>>>>>>>>>>> context) => context.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent;
    }
}
