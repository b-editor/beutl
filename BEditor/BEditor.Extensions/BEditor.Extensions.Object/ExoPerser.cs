using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using BEditor.Core.Data;

using EntryMap = System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>>;

namespace BEditor.Extensions.Object
{
    public class ExoPerser
    {
        internal const string returnStr = "\r\n";
        private readonly EntryMap map = new EntryMap();

        private ExoPerser(string text)
        {
            Text = text;
            Load();
        }

        public string Text { get; }
        public List<string> Sections => map.Keys.ToList();
        public string this[string section, string key]
        {
            get
            {
                if (HasKey(section, key))
                {
                    return map[section][key];
                }
                return string.Empty;
            }
        }
        public Dictionary<string, string> this[string section]
        {
            get
            {
                if (HasSection(section))
                {
                    return map[section];
                }

                throw new KeyNotFoundException();
            }
        }

        public static ExoPerser FromFile(string file)
        {
            var encode = CodePagesEncodingProvider.Instance.GetEncoding("shift-jis") ?? throw new Exception("エンコードが見つかりませんでした");
            // Todo: 最大ファイルサイズが少ないので変更する
            var text = File.ReadAllText(file, encode);

            return new ExoPerser(text);
        }
        public Project? Perse()
        {
            var exedit = new ExeditHeader(this["exedit"]);
            var exos = new List<ExobjectHeader>();
            var exes = new List<RawExeffect>();

            foreach (var item in map.Where(i => !i.Key.Contains('.') && !i.Key.Contains("exedit")))
            {
                exos.Add(new(item.Value));
            }
            foreach (var item in map.Where(i => i.Key.Contains('.') && !i.Key.Contains("exedit")))
            {
                exes.Add(new(
                    int.Parse(item.Key.Split('.')[0]),
                    item.Value));
            }

            var items = exos.Zip(exes.GroupBy(i => i.Number), (exo, exe) => new Exobject(exo, exe.ToList())).ToList();
            var proj = exedit.ToProject();
            var scene = proj.PreviewScene;

            foreach (var clip in items) scene.Add(clip.ToClip(scene));

            return proj;
        }

        private void Load()
        {
            //Commentを除く
            string section = "";
            var rsection = new Regex("^[^;]*\\[(?<section>.*?)\\]");
            var comment = new Regex("^(?<ucmt>[^;]*?);(?<cmt>.*?)$");
            var keyvalue = new Regex("^(?<key>.*?)=(?<value>.*?)$");


            foreach (var line in Text.Split(returnStr))
            {
                //Section解析
                if (rsection.IsMatch(line))
                {
                    section = rsection.Match(line).Groups["section"].ToString();
                    map[section] = new Dictionary<string, string>();
                }
                else if (string.IsNullOrEmpty(section))
                {
                    continue;
                }
                //Section登録されていればKey = Valueを検索する
                else if (comment.IsMatch(line))
                {
                    string uc = comment.Match(line).Groups["ucmt"].ToString();
                    if (keyvalue.IsMatch(uc))
                    {
                        AddParam(section,
                            keyvalue.Match(uc).Groups["key"].ToString(),
                            keyvalue.Match(uc).Groups["value"].ToString());
                    }
                }
                else if (keyvalue.IsMatch(line))
                {
                    AddParam(section,
                        keyvalue.Match(line).Groups["key"].ToString(),
                        keyvalue.Match(line).Groups["value"].ToString());
                }
            }
        }
        public List<string> Keys(string section)
        {
            if (map.ContainsKey(section))
            {
                return map[section].Keys.ToList();
            }
            return new List<string>();
        }
        private bool HasKey(string section, string key)
        {
            return map.ContainsKey(section) && map[section].ContainsKey(key);
        }
        private bool HasSection(string section)
        {
            return map.ContainsKey(section);
        }
        private void AddParam(string section, string key, string value)
        {
            if (key.Trim().Length > 0)
            {
                map[section].Add(key.Trim(), value.Trim());
            }
        }
    }
}
