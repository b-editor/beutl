using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.Data.Property;

namespace BEditor
{
    public class ScriptContext
    {
        private readonly Project _project;

        public ScriptContext(Project project)
        {
            _project = project;
        }

        public string? Directory => _project.DirectoryName;
        public string? Name => _project.Name;
        public int Samplingrate => _project.Samplingrate;
        public int Framerate => _project.Framerate;
        public Scene Scene
        {
            get => _project.PreviewScene;
            set => _project.PreviewScene = value;
        }
        public IReadOnlyList<Scene> Scenes => _project.SceneList;
        public ClipElement? Clip
        {
            get => Scene.SelectItem;
            set => Scene.SelectItem = value;
        }
        public IReadOnlyList<ClipElement> Clips => Scene.Datas;

        public void Save(string? file = null)
        {
            if ((file is null) ? _project.Save() : _project.Save(file))
            {
                Console.WriteLine("保存しました。");
            }
            else
            {
                Console.WriteLine("保存出来ませんでした。");
            }
        }
        public void List(IReadOnlyList<object> items)
        {
            void ListScene(IReadOnlyList<Scene> scenes)
            {
                foreach (var scene in scenes)
                {
                    Console.Write("* " + scene.SceneName);

                    if (scene == _project.PreviewScene)
                    {
                        Console.Write(" <= Selected");
                    }

                    Console.Write('\n');
                }
            }
            void ListClip(IReadOnlyList<ClipElement> clips)
            {
                Console.WriteLine("管理名 | 名前");
                Console.WriteLine("=============");

                foreach (var clip in clips)
                {
                    Console.Write("* " + clip.Name + " | " + clip.LabelText);

                    if (clip == _project.PreviewScene.SelectItem)
                    {
                        Console.Write(" <= Selected");
                    }

                    Console.Write('\n');
                }
            }
            void ListEffect(IReadOnlyList<EffectElement> effects)
            {
                foreach (var effect in effects)
                {
                    Console.Write("* " + effect.Name + " | " + (effect.IsEnabled ? "enable" : "disable"));

                    Console.Write('\n');
                }
            }

            switch (items)
            {
                case IReadOnlyList<Scene> scenes:
                    ListScene(scenes);
                    break;
                case IReadOnlyList<ClipElement> clips:
                    ListClip(clips);
                    break;
                case IReadOnlyList<EffectElement> effect:
                    ListEffect(effect);
                    break;
                default:
                    Console.WriteLine("このデータはリスト表示できません。");
                    break;
            }
        }
        public void Prop(string path)
        {
            // [Effect][Property]の場合
            var regex1 = new Regex(@"^\[([\d]+)\]\[([\d]+)\]\z");
            // [Effect][Group][Property]の場合
            var regex2 = new Regex(@"^\[([\d]+)\]\[([\d]+)\]\[([\d]+)\]\z");
            PropertyElement? prop = null;

            if (regex1.IsMatch(path))
            {
                var match = regex1.Match(path);

                var effect = int.TryParse(match.Groups[1].Value, out var id) ? Clip?.Find(id) : null;
                prop = int.TryParse(match.Groups[2].Value, out var id1) ? effect?.Find(id1) : null;
            }
            else if (regex2.IsMatch(path))
            {
                var match = regex2.Match(path);

                var effect = int.TryParse(match.Groups[1].Value, out var id) ? Clip?.Find(id) : null;
                var parent = int.TryParse(match.Groups[2].Value, out var id1) ? effect?.Find(id1) as IParent<PropertyElement> : null;
                prop = int.TryParse(match.Groups[3].Value, out var id2) ? parent?.Find(id2) : null;
            }

            // Convert to json.
            if (prop is not null)
            {
                using var memory = new MemoryStream();
                if (!Serialize.SaveToStream(prop, memory, SerializeMode.Json))
                {
                    Console.WriteLine("プロパティをJsonに変換できませんでした。");

                    return;
                }

                Console.WriteLine(Encoding.UTF8.GetString(memory.ToArray()));
            }
            else
            {
                Console.WriteLine("プロパティが見つかりませんでした。");
            }
        }
        public void Add(Range range, int layer, ObjectMetadata metadata, bool setcurrent = false)
        {

        }
        /*
Prop(path)
Add(range, layer, type[, setcurrent = false])
Add(type)
Add(width, height, background)
Remove(clip)
Remove(effect)
Move(layer)
Move(range)
HideLayer(layer)
ShowFrame()
Undo([count = 1])
Redo([count = 1])
Encode(file)
EncodeImg(file, frame)
         */
    }
}
