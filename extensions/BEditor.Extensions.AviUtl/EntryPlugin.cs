using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Plugin;

using Microsoft.Extensions.DependencyInjection;

using Neo.IronLua;

namespace BEditor.Extensions.AviUtl
{
    public interface IScriptSettings
    {
        public string Name { get; }

        public string Variable { get; }

        public PropertyElement ToProperty();

        public PropertyElement ToProperty(JsonElement json);
    }

    public record Track(string Name, int Number, float Max, float Min, float Default, string Unit) : IScriptSettings
    {
        // --track0:name,min,max,default
        private static readonly Regex _track = new(@"^--track(?<number>.*?):(?<name>.*?),(?<values>.*?)$");

        public string Variable { get; } = "track" + Number;

        public static Track Parse(string line)
        {
            var match = _track.Match(line);

            try
            {
                var num = int.Parse(match.Groups["number"].Value);
                var name = match.Groups["name"].Value;
                var values = match.Groups["values"].Value.Split(',');
                var min = float.Parse(values[0]);
                var max = float.Parse(values[1]);
                var def = float.Parse(values[2]);
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

        public PropertyElement ToProperty()
        {
            return new EaseProperty(new(Name, Default, Max, Min));
        }

        public PropertyElement ToProperty(JsonElement json)
        {
            var prop = ToProperty();
            prop.SetObjectData(json);
            return prop;
        }
    }

    public record Param(Dictionary<string, string> Variables) : IScriptSettings
    {
        // --param:dx=10;dy=20;
        private static readonly Regex _param = new(@"^--param:(?<content>.*?)$");

        public string Name => "Param";

        public string Variable => Name;

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

        public PropertyElement ToProperty()
        {
            return new TextProperty(new(Name, new string(Variables.Select(i => $"{i.Key}={i.Value};").SelectMany(i => i).ToArray())));
        }

        public PropertyElement ToProperty(JsonElement json)
        {
            var prop = ToProperty();
            prop.SetObjectData(json);
            return prop;
        }
    }

    public record ColorSettings(Color Color) : IScriptSettings
    {
        // --color:0xffffff
        private static readonly Regex _color = new(@"^--color:0x(?<color>.*?)$");

        public string Name => "Color";

        public string Variable => "color";

        public static ColorSettings Parse(string line)
        {
            var match = _color.Match(line);

            var color = Color.Parse("#" + match.Groups["color"].Value);
            color.A = 255;

            return new(color);
        }

        public static bool IsMatch(string line)
        {
            return _color.IsMatch(line);
        }

        public PropertyElement ToProperty()
        {
            return new ColorProperty(new(Name, Color));
        }

        public PropertyElement ToProperty(JsonElement json)
        {
            var prop = ToProperty();
            prop.SetObjectData(json);
            return prop;
        }
    }

    public record FileReference() : IScriptSettings
    {
        // --file:
        private static readonly Regex _file = new(@"^--file:$");

        public string Name => "File";

        public string Variable => "file";

        public static bool IsMatch(string line)
        {
            return _file.IsMatch(line);
        }

        public PropertyElement ToProperty()
        {
            return new FileProperty(new(Name));
        }

        public PropertyElement ToProperty(JsonElement json)
        {
            var prop = ToProperty();
            prop.SetObjectData(json);
            return prop;
        }
    }

    public record CheckBox(string Name, int Number, bool IsChecked) : IScriptSettings
    {
        // --check0:name,default
        private static readonly Regex _check = new(@"^--check(?<number>.*?):(?<name>.*?),(?<default>.*?)$");

        public string Variable { get; } = $"check{Number}";

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

        public PropertyElement ToProperty()
        {
            return new CheckProperty(new(Name, IsChecked));
        }

        public PropertyElement ToProperty(JsonElement json)
        {
            var prop = ToProperty();
            prop.SetObjectData(json);
            return prop;
        }
    }

    public record DialogSettings(DialogSection[] Sections) : IScriptSettings
    {
        // --dialog:section0,s0=100;section1,s1=100;
        private static readonly Regex _dialog = new(@"^--dialog:(?<content>.*?)$");
        private static readonly Regex _section = new(@"^(?<name>.*?),(?<var>.*?)=(?<value>.*?)$");

        public string Name => "Dialog";

        public string Variable => Name;

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

        public PropertyElement ToProperty()
        {
            return new DynamicDialog(this);
        }

        public PropertyElement ToProperty(JsonElement json)
        {
            var prop = ToProperty();
            prop.SetObjectData(json);
            return prop;
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

        public PropertyElement ToProperty()
        {
            return Type switch
            {
                DialogSectionType.None => new TextProperty(new(Name, Value)),
                DialogSectionType.CheckBox => new CheckProperty(new(Name, Value is "true" or "1")),
                DialogSectionType.Color => new ColorProperty(new(Name, GetColor())),
                _ => throw new NotImplementedException(),
            };
        }

        public PropertyElement ToProperty(JsonElement json)
        {
            var prop = ToProperty();
            prop.SetObjectData(json);
            return prop;
        }

        private Color GetColor()
        {
            // col="" 対策
            if (Value is "\"\"")
            {
                return Colors.White;
            }

            // 0x000000 など
            var color = Color.Parse("#" + Value.Replace("0x", ""));
            color.A = 255;

            return color;
        }
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

        public string? Parent
        {
            get
            {
                var parent = Directory.GetParent(File)?.Name;

                return parent is "script" ? null : parent;
            }
        }
    }

    public class Plugin : PluginObject
    {
        [AllowNull]
        internal static ScriptLoader Loader;

        [AllowNull]
        internal static Plugin Default;

        private SettingRecord? settings;

        public Plugin(PluginConfig config) : base(config)
        {
            Default = this;
        }

        public override string PluginName => "BEditor.Extensions.AviUtl";

        public override string Description => string.Empty;

        public override SettingRecord Settings
        {
            get => settings ??= SettingRecord.LoadFrom<CustomSettings>(Path.Combine(BaseDirectory, "settings.json")) ?? new CustomSettings();
            set => (settings = value).Save(Path.Combine(BaseDirectory, "settings.json"));
        }

        public override Guid Id { get; } = Guid.Parse("138CE66C-3E1D-4BF8-A879-F3272C8FFE05");

        public static void Register()
        {
            var dir = Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly().Location)!.FullName, "script");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Loader = new(dir);
            var items = Loader.Load();

            PluginBuilder.Configure<Plugin>()
                .ConfigureServices(s => s
                    .AddSingleton(_ => Loader.Global)
                    .AddSingleton(_ => Loader))
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

            list.AddRange(anm
                .Where(i => i.Parent is not null)
                .GroupBy(i => i.Parent)
                .Select(i =>
                {
                    var list = i.Where(e => e.GroupName is not null)
                        .GroupBy(e => e.GroupName)
                        .Select(e => new EffectMetadata(e.Key!)
                        {
                            Children = e.Select(p => new EffectMetadata(p.Name, () => new AnimationEffect(p), typeof(AnimationEffect))).ToArray()
                        })
                        .ToList();

                    list.AddRange(i.Where(e => e.GroupName is null)
                        .Select(e => new EffectMetadata(e.Name, () => new AnimationEffect(e), typeof(AnimationEffect))));

                    return new EffectMetadata(i.Key!)
                    {
                        Children = list
                    };
                }));

            list.AddRange(anm
                .Where(i => i.Parent is null)
                .Select(i => new EffectMetadata(i.Name, () => new AnimationEffect(i), typeof(AnimationEffect))));

            return metadata;
        }
    }

    public record CustomSettings(
        [property: DisplayName("Y軸の値を反転する (左手座標系を使用)")]
        bool ReverseYAsis = true) : SettingRecord;
}