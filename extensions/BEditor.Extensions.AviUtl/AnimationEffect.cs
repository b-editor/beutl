using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Extensions.AviUtl.Resources;
using BEditor.Media;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BEditor.Extensions.AviUtl
{
    public class AnimationEffect : ImageEffect
    {
        public static readonly DirectProperty<AnimationEffect, string> ScriptNameProperty = EditingProperty.RegisterDirect<string, AnimationEffect>(
            nameof(ScriptName),
            owner => owner.ScriptName,
            (owner, obj) => owner.ScriptName = obj,
            EditingPropertyOptions<string>.Create()!.Serialize()!);

        public static readonly DirectProperty<AnimationEffect, string?> GroupNameProperty = EditingProperty.RegisterDirect<string?, AnimationEffect>(
            nameof(GroupName),
            owner => owner.GroupName,
            (owner, obj) => owner.GroupName = obj,
            EditingPropertyOptions<string?>.Create().Serialize());

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
            if (Parent.Effect[0] is ImageObject obj)
            {
                var lua = Plugin.Loader.Global;
                var table = new ObjectTable(args, obj);
                SetPropertyValue(table, args.Frame);
                lua.SetValue("obj", table);
                lua.SetValue("rand", new ObjectTable.RandomDelegate(table.rand));

                try
                {
                    var result = lua.DoChunk(Entry.Code, Entry.File);
                }
                catch (Exception e)
                {
                    ServicesLocator.Current.Logger.LogError(e, Strings.FailedToExecuteScript);
                    ServiceProvider?.GetService<IMessage>()?.Snackbar(Strings.FailedToExecuteScript);
                }
            }
            Parent.Parent.GraphicsContext!.PlatformImpl.MakeCurrent();
        }

        public override IEnumerable<PropertyElement> GetProperties()
        {
            return Properties.Select(i => i.Value);
        }

        public override void SetObjectData(JsonElement element)
        {
            base.SetObjectData(element);
            Properties = new();
            Entry = Plugin.Loader.Loaded!.First(i => i.Name == ScriptName && i.GroupName == GroupName);

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

        private void SetPropertyValue(ObjectTable table, Frame frame)
        {
            if (Properties.TryGetValue("track0", out var prop) && prop is EaseProperty track0)
            {
                table.track0 = track0[frame];
            }
            if (Properties.TryGetValue("track1", out prop) && prop is EaseProperty track1)
            {
                table.track1 = track1[frame];
            }
            if (Properties.TryGetValue("track2", out prop) && prop is EaseProperty track2)
            {
                table.track2 = track2[frame];
            }
            if (Properties.TryGetValue("track3", out prop) && prop is EaseProperty track3)
            {
                table.track3 = track3[frame];
            }
            if (Properties.TryGetValue("check0", out prop) && prop is CheckProperty check0)
            {
                table.check0 = check0.Value;
            }
            if (Properties.TryGetValue("color", out prop) && prop is ColorProperty color)
            {
                var value = (BGRA32)color.Value;
                table.color = Unsafe.As<BGRA32, int>(ref value);
            }
            if (Properties.TryGetValue("file", out prop) && prop is FileProperty file)
            {
                Plugin.Loader.Global.SetValue("file", file.Value);
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