using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

using BEditor.Data;
using BEditor.Drawing;
using BEditor.Plugin;

using Microsoft.Extensions.DependencyInjection;

using Neo.IronLua;

namespace BEditor.Extensions.AviUtl
{
    public interface IScriptSettings
    {
        public string Name { get; }
    }

    public record Track(string Name, int Number, float Max, float Min, float Default, string Unit) : IScriptSettings
    {
        // --track0:name,default,max,min
        private static readonly Regex _track = new(@"^--track(?<number>.*?):(?<name>.*?),(?<values>.*?)$");

        public static Track Parse(string line)
        {
            var match = _track.Match(line);

            try
            {
                var num = int.Parse(match.Groups["number"].Value);
                var name = match.Groups["name"].Value;
                var values = match.Groups["values"].Value.Split(',');
                var def = float.Parse(values[0]);
                var max = float.Parse(values[1]);
                var min = float.Parse(values[2]);
                var unit = values.Length is 4 ? values[3] : "0.01";

                return new(name, num, max, min, def, unit);
            }
            catch (Exception e)
            {
                throw new NotSupportedException($"{line} は対応していません。", e);
            }
        }

        public static bool IsMatch(string line)
        {
            return _track.IsMatch(line);
        }
    }

    public record Param(Dictionary<string, string> Variables) : IScriptSettings
    {
        // --param:dx=10;dy=20;
        private static readonly Regex _param = new(@"^--param:(?<content>.*?)$");

        string IScriptSettings.Name => string.Empty;

        public static Param Parse(string line)
        {
            var content = _param.Match(line).Groups["content"].Value;

            return new(content.Split(';')
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Select(i => i.Split('='))
                .ToDictionary(i => i[0], i => i[1]));
        }

        public static bool IsMatch(string line)
        {
            return _param.IsMatch(line);
        }
    }

    public record ColorSettings(Color Color) : IScriptSettings
    {
        // --color:0xffffff
        private static readonly Regex _color = new(@"^--color:0x(?<color>.*?)$");

        string IScriptSettings.Name => string.Empty;

        public static ColorSettings Parse(string line)
        {
            var match = _color.Match(line);

            var color = Color.FromHTML("#" + match.Groups["color"].Value);
            color.A = 255;

            return new(color);
        }

        public static bool IsMatch(string line)
        {
            return _color.IsMatch(line);
        }
    }

    public record FileReference() : IScriptSettings
    {
        // --file:
        private static readonly Regex _file = new(@"^--file:$");

        string IScriptSettings.Name => string.Empty;

        public static bool IsMatch(string line)
        {
            return _file.IsMatch(line);
        }
    }

    public record CheckBox(string Name, int Number, bool IsChecked) : IScriptSettings
    {
        // --check0:name,default
        private static readonly Regex _check = new(@"^--check(?<number>.*?):(?<name>.*?),(?<default>.*?)$");

        public static CheckBox Parse(string line)
        {
            var match = _check.Match(line);
            var num = int.Parse(match.Groups["number"].Value);
            var name = match.Groups["name"].Value;
            var def = match.Groups["default"].Value;

            return new(name, num, def is "true" or "1");
        }

        public static bool IsMatch(string line)
        {
            return _check.IsMatch(line);
        }
    }

    public record DialogSettings(DialogSection[] Sections) : IScriptSettings
    {
        // --dialog:section0,s0=100;section1,s1=100;
        private static readonly Regex _dialog = new(@"^--dialog:(?<content>.*?)$");
        private static readonly Regex _section = new(@"^(?<name>.*?),(?<var>.*?)=(?<value>.*?)$");

        string IScriptSettings.Name => string.Empty;


        public static DialogSettings Parse(string line)
        {
            var content = _dialog.Match(line).Groups["content"].Value;

            return new(content.Split(';')
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Select(i => _section.Match(i).Groups)
                .Select(i => new DialogSection(i["name"].Value, i["var"].Value, i["value"].Value))
                .ToArray());
            /*
             .ToDictionary(
                    i => new KeyValuePair<string, string>(i["name"].Value, i["var"].Value),
                    i => i["value"].Value)
             */
        }

        public static bool IsMatch(string line)
        {
            return _dialog.IsMatch(line);
        }
    }

    public enum DialogSectionType
    {
        None,
        CheckBox,
        Color
    }

    public class DialogSection
    {
        public DialogSection(string name, string var, string value)
        {
            var a = name.Split('/');
            Name = a[0];
            Variable = var;
            Value = value;

            if (a.Length is 2)
            {
                Type = a[1] switch
                {
                    "chk" => DialogSectionType.CheckBox,
                    "col" => DialogSectionType.Color,
                    _ => DialogSectionType.None
                };
            }
        }

        public string Name { get; }
        public string Variable { get; }
        public string Value { get; }
        public DialogSectionType Type { get; } = DialogSectionType.None;
    }

    public enum ScriptType
    {
        Object,
        Animation,
        Camera,
        SceneChange,
        TrackBar
    }

    public record ScriptEntry(string Name, string File, string Code, IScriptSettings[] Settings, string? GroupName = null)
    {
        private ScriptType? _type;

        public ScriptType Type => _type ??= Path.GetExtension(File) switch
        {
            ".anm" => ScriptType.Animation,
            ".cam" => ScriptType.Camera,
            ".obj" => ScriptType.Object,
            ".scn" => ScriptType.SceneChange,
            ".tra" => ScriptType.TrackBar,
            _ => ScriptType.Animation
        };
    }

    public class Plugin : PluginObject
    {
        [AllowNull]
        internal static ScriptLoader _loader;

        public Plugin(PluginConfig config) : base(config)
        {
        }

        public override string PluginName => "BEditor.Extensions.AviUtl";

        public override string Description => string.Empty;

        public override SettingRecord Settings { get; set; } = new CustomSettings();

        public static void Register(string[] args)
        {
            var dir = Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly().Location)!.FullName, "script");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            _loader = new(dir);
            var items = _loader.Load();

            PluginBuilder.Configure<Plugin>()
                .ConfigureServices(s => s.AddSingleton(_ => LuaScript.LuaGlobal))
                .With(CreateEffectMetadata(items.Where(i => i.Type is ScriptType.Animation)))
                .Register();
        }

        private static EffectMetadata CreateEffectMetadata(IEnumerable<ScriptEntry> anm)
        {
            var list = new List<EffectMetadata> { EffectMetadata.Create<LuaScript>("スクリプト制御") };
            var metadata = new EffectMetadata("AviUtl")
            {
                Children = list
            };

            list.AddRange(anm.Where(i => i.GroupName is not null)
                .GroupBy(i => i.GroupName)
                .Select(i => new EffectMetadata(i.Key!)
                {
                    Children = i.Select(e => new EffectMetadata(e.Name, () => new AnimationEffect(e), typeof(AnimationEffect))).ToArray()
                }));

            list.AddRange(anm
                .Where(i => i.GroupName is null)
                .Select(i => new EffectMetadata(i.Name, () => new AnimationEffect(i), typeof(AnimationEffect))));

            return metadata;
        }
    }

    public record CustomSettings(
        [property: DisplayName("Y軸の値を反転する")]
        bool ReverseYAsis = true) : SettingRecord;
}