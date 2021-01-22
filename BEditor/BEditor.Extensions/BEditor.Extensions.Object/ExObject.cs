using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

using BEditor.Core.Data;

namespace BEditor.Extensions.Object
{
    public class Exobject
    {
        private static readonly PropertyInfo GetNewId = typeof(Scene).GetRuntimeProperties().ToList().Find(i => i.Name == "NewId")!;

        public Exobject(ExobjectHeader header, List<RawExeffect> exeffects)
        {
            Header = header;
            RawEffects = exeffects;
        }

        public ExobjectHeader Header { get; }
        public List<RawExeffect> RawEffects { get; }

        public ClipData ToClip(Scene scene)
        {
            var id = (int)GetNewId.GetValue(scene)!;
            var clip = new ClipData(id, new(), Header.Start, Header.End, null!, Header.Layer, scene);


            return clip;
        }
    }
}
