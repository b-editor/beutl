using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Extensions.AviUtl
{
    public class AnimationEffect : ImageEffect
    {
        public static readonly DirectEditingProperty<AnimationEffect, (string script, string? group)> ScriptProperty = EditingProperty.RegisterDirect<(string script, string? group), AnimationEffect>(
            "Script",
            owner => (owner.ScriptName, owner.GroupName),
            (owner, obj) => (owner.ScriptName, owner.GroupName) = obj,
            serializer: new EditingPropertySerializer<(string script, string? group)>(
                (writer, obj) =>
                {
                    writer.WriteString(nameof(ScriptName), obj.script);
                    writer.WriteString(nameof(GroupName), obj.group);
                },
                element => (element.GetProperty(nameof(ScriptName)).GetString()!, element.GetProperty(nameof(GroupName)).GetString())));

        public AnimationEffect(ScriptEntry entry)
        {
            Entry = entry;
            ScriptName = entry.Name;
            GroupName = entry.GroupName;
        }

        public override string Name => ScriptName;

        public ScriptEntry Entry { get; private set; }

        public string ScriptName { get; private set; }

        public string? GroupName { get; private set; }

        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {

        }

        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield break;
        }

        protected override void OnLoad()
        {
            base.OnLoad();
            Entry = Plugin._loader.Loaded!.First(i => i.Name == ScriptName && i.GroupName == GroupName);
        }
    }
}