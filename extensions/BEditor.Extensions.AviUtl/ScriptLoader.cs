using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Neo.IronLua;

namespace BEditor.Extensions.AviUtl
{
    public class ScriptLoader
    {
        private static readonly Regex _scriptName = new(@"^\@(?<name>.*?)$", RegexOptions.Multiline);
        private static readonly Encoding shift_jis = CodePagesEncodingProvider.Instance.GetEncoding("shift-jis")!;

        public ScriptLoader(string baseDir)
        {
            BaseDirectory = baseDir;
            Engine = new();
            Global = Engine.CreateEnvironment();
        }

        public Lua Engine { get; }

        public LuaGlobal Global { get; }

        public string BaseDirectory { get; }

        public ScriptEntry[]? Loaded { get; private set; }

        public ScriptEntry[] Load()
        {
            return Loaded ??= Directory.EnumerateFiles(BaseDirectory, "*.*", SearchOption.AllDirectories)
                .Where(i => Path.GetExtension(i) is ".anm" or ".cam" or ".obj" or ".scn" or ".tra")
                .Select(i => LoadFile(i))
                .SelectMany(i => i)
                .ToArray();
        }

        private static IEnumerable<ScriptEntry> LoadFile(string file)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            // 複数の場合
            if (name[0] is '@')
            {
                var lines = File.ReadAllLines(file, shift_jis);
                var text = string.Join('\n', lines);

                var array = lines.Select((line, index) => (line, index)).Where(i => _scriptName.IsMatch(i.line)).ToArray();
                for (int i = 0; i < array.Length - 1; i++)
                {
                    var (line, index) = array[i];
                    var code = string.Join('\n', lines.AsSpan(index + 1, array[i + 1].index - index - 1).ToArray());

                    yield return LoadEntry(line, code, file, name);
                }

                var codelast = string.Join('\n', lines.AsSpan(array[^1].index + 1).ToArray());
                yield return LoadEntry(array[^1].line, codelast, file, name);
            }
            else
            {
                yield return LoadEntry(name, File.ReadAllText(file, shift_jis), file, null);
            }
        }

        private static ScriptEntry LoadEntry(string name, string code, string file, string? group)
        {
            return new(name, file, code, code
                .Replace("\r\n", "\n")
                .Split("\n")
                .Select(i => MatchSettings(i))
                .Where(i => i is not null)
                .ToArray()!, group);
        }

        private static IScriptSettings? MatchSettings(string line)
        {
            if (Track.IsMatch(line)) return Track.Parse(line);
            else if (Param.IsMatch(line)) return Param.Parse(line);
            else if (ColorSettings.IsMatch(line)) return ColorSettings.Parse(line);
            else if (FileReference.IsMatch(line)) return new FileReference();
            else if (CheckBox.IsMatch(line)) return CheckBox.Parse(line);
            else if (DialogSettings.IsMatch(line)) return DialogSettings.Parse(line);
            else return null;
        }
    }
}