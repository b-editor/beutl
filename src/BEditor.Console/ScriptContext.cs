using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Media;
using BEditor.Media.Encoder;
using BEditor.Properties;

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
                Console.WriteLine(ConsoleResources.SavedTo, file);
            }
            else
            {
                Console.WriteLine(ConsoleResources.FailedToSave);
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
                Console.WriteLine($"{ConsoleResources.ManagementName} | {Resources.Name}");
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
                    Console.WriteLine(ConsoleResources.This_data_cannot_be_displayed_in_a_list_);
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
                    Console.WriteLine(ConsoleResources.FailedToConvertPropertiesToJson);

                    return;
                }

                Console.WriteLine(Encoding.UTF8.GetString(memory.ToArray()));
            }
            else
            {
                Console.WriteLine(ConsoleResources.PropertyNotFound);
            }
        }
        public void Add(Range range, int layer, string metadata, bool setcurrent = false)
        {
            var start = range.Start.Value;
            var end = range.End.IsFromEnd ? Scene.TotalFrame.Value : range.End.Value;

            if (!Scene.InRange(start, end, layer))
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine(ConsoleResources.ClipExistsInTheSpecifiedLocation);

                Console.ResetColor();
            }
            else
            {
                var meta = ObjectMetadata.LoadedObjects.FirstOrDefault(i => i.Name == metadata);

                if (meta is null)
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine(ConsoleResources.NotFound, metadata);

                    Console.ResetColor();

                    return;
                }

                Scene.AddClip(start, layer, meta, out var clip).Execute();

                clip.End = end;

                if (setcurrent)
                {
                    Scene.SetCurrentClip(clip);
                }

                Console.WriteLine(ConsoleResources.AddedClip, start, end, layer);
            }
        }
        public void Add(int width, int height)
        {
            var scene = new Scene(width, height)
            {
                SceneName = $"Scene{_project.SceneList.Count}",
                Parent = _project
            };
            scene.Load();
            _project.SceneList.Add(scene);
            _project.PreviewScene = scene;
        }
        public void Add(string effect)
        {
            if (Clip is null)
            {
                Console.WriteLine(ConsoleResources.SelectClip);

                return;
            }

            EffectMetadata? meta = EffectMetadata.LoadedEffects
                .SelectMany(i => i.Children?
                    .Select(i2 => new EffectItem(i2, i.Name)) ?? new EffectItem[] { new(i, null) })
                    .FirstOrDefault(i=>i.Name == effect)?.Metadata;

            if (meta is null)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine(ConsoleResources.NotFound, effect);

                Console.ResetColor();

                return;
            }

            var effectelm = meta.CreateFunc();
            Clip.AddEffect(effectelm).Execute();

            Console.WriteLine(ConsoleResources.AddedEffect);
        }
        public void Remove(ClipElement clip)
        {
            clip.Parent.RemoveClip(clip).Execute();

            Clip = Clips.Count is not 0 ? Clips[0] : null;
            Console.WriteLine(ConsoleResources.RemovedClip);
        }
        public void Remove(int effectIndex)
        {
            if (Clip is null)
            {
                Console.WriteLine(ConsoleResources.SelectClip);

                return;
            }
            if (Clip.Effect.Count >= effectIndex)
            {
                Console.WriteLine(ConsoleResources.IndexIsOutOfRange);

                return;
            }

            Clip.RemoveEffect(Clip.Effect[effectIndex]).Execute();

            Console.WriteLine(ConsoleResources.RemovedEffect);
        }
        public void Move(int layer)
        {
            if (Clip is null)
            {
                Console.WriteLine(ConsoleResources.SelectClip);

                return;
            }

            if (!Clip.Parent.InRange(Clip.Start, Clip.End, Clip.Layer))
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine(ConsoleResources.ClipExistsInTheSpecifiedLocation);

                Console.ResetColor();

                return;
            }

            Clip.MoveFrameLayer(Clip.Start, layer).Execute();

            Console.WriteLine(ConsoleResources.MovedClip);
        }
        public void Move(Range range)
        {
            if (Clip is null)
            {
                Console.WriteLine(ConsoleResources.SelectClip);

                return;
            }

            var start = range.Start.Value;
            var end = range.End.IsFromEnd ? Scene.TotalFrame.Value : range.End.Value;

            if (!Clip.Parent.InRange(start, end, Clip.Layer))
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine(ConsoleResources.ClipExistsInTheSpecifiedLocation);

                Console.ResetColor();

                return;
            }

            Clip.ChangeLength(start, end).Execute();

            Console.WriteLine(ConsoleResources.MovedClip);
        }
        public void HideLayer(int layer)
        {
            Scene.HideLayer.Remove(layer);
            Scene.HideLayer.Add(layer);

            Console.WriteLine(ConsoleResources.LayerIsHidden, layer);
        }
        public static void Undo(int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                CommandManager.Undo();
            }
        }
        public static void Redo(int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                CommandManager.Redo();
            }
        }
        public void Encode(string file)
        {
            using var encoder = new FFmpegEncoder(Scene.Width, Scene.Height, _project.Framerate, VideoCodec.Default, file);
            using (var progress = new ProgressBar())
            {
                var total = Scene.TotalFrame + 1;

                for (Frame frame = 0; frame < Scene.TotalFrame; frame++)
                {
                    using var img = Scene.Render(frame, RenderType.VideoOutput).Image;

                    encoder.Write(img);

                    progress.Report((double)frame / total);
                }
            }

            Console.WriteLine(ConsoleResources.SavedTo, file);
        }
        public void EncodeImg(string file, Frame frame)
        {
            using var img = Scene.Render(frame, RenderType.ImageOutput).Image;

            img.Encode(file);

            Console.WriteLine(string.Format(ConsoleResources.SavedTo, file));
        }

        public record EffectItem(EffectMetadata Metadata, string? ParentName)
        {
            public string Name => ParentName is null ? Metadata.Name : $"{ParentName}.{Metadata.Name}";
        }
    }
}
