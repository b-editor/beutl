using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

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

            foreach (var item in Entry.Settings)
            {
                Properties.Add(item.Variable, item.ToProperty());
            }
        }

        public override string Name => ScriptName;

        public ScriptEntry Entry { get; private set; }

        public string ScriptName { get; private set; }

        public string? GroupName { get; private set; }

        public Dictionary<string, PropertyElement> Properties { get; private set; } = new();

        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {

        }

        public override IEnumerable<PropertyElement> GetProperties()
        {
            return Properties.Select(i => i.Value);
        }

        public override void SetObjectData(JsonElement element)
        {
            base.SetObjectData(element);
            Properties = new();
            Entry = Plugin._loader.Loaded!.First(i => i.Name == ScriptName && i.GroupName == GroupName);

            foreach (var item in Entry.Settings)
            {
                if (element.TryGetProperty(item.Variable, out var val))
                {
                    SetOrAddDictionary(Properties, item.Variable, item.ToProperty(val));
                }
                else
                {
                    SetOrAddDictionary(Properties, item.Variable, item.ToProperty());
                }
            }
        }

        internal static void SetOrAddDictionary(Dictionary<string, PropertyElement> dictionary, string key, PropertyElement value)
        {
            if (dictionary.ContainsKey(key))
            {
                dictionary[key] = value;
            }
            else
            {
                dictionary.Add(key, value);
            }
        }
    }

    public class DynamicDialog : DialogProperty
    {
        public DynamicDialog(DialogSettings dialog)
        {
            Dialog = dialog;
            PropertyMetadata = new("ダイアログを表示");

            foreach (var item in dialog.Sections)
            {
                Properties.Add(item.Variable, item.ToProperty());
            }
        }

        public DialogSettings Dialog { get; private set; }

        public Dictionary<string, PropertyElement> Properties { get; private set; } = new();

        public override EffectElement Parent
        {
            get => base.Parent;
            set
            {
                if (value is AnimationEffect anm)
                {
                    Dialog = (DialogSettings)anm.Entry.Settings.First(i => i is DialogSettings);
                }

                base.Parent = value;
            }
        }

        public override IEnumerable<PropertyElement> GetProperties()
        {
            return Properties.Select(i => i.Value);
        }

        public override void SetObjectData(JsonElement element)
        {
            base.SetObjectData(element);
            Properties = new();
            PropertyMetadata = new("ダイアログを表示");

            foreach (var item in Dialog.Sections)
            {
                if (element.TryGetProperty(item.Variable, out var val))
                {
                    AnimationEffect.SetOrAddDictionary(Properties, item.Variable, item.ToProperty(val));
                }
                else
                {
                    AnimationEffect.SetOrAddDictionary(Properties, item.Variable, item.ToProperty());
                }
            }
        }
    }
}