using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

using BEditor.Command;
using BEditor.Data;
using BEditor.Models.Extension;
using BEditor.Primitive;
using BEditor.Primitive.Objects;
using BEditor.Properties;
using BEditor.ViewModels.CreatePage;
using BEditor.Views;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Reactive.Bindings;

namespace BEditor.Models
{
    public class EditModel
    {
        public static readonly EditModel Current = new();
        private static readonly ILogger Logger = AppData.Current.LoggingFactory.CreateLogger<EditModel>();

        private EditModel()
        {
            Undo.Where(_ => AppData.Current.Project is not null)
                .Subscribe(_ =>
            {
                CommandManager.Undo();

                AppData.Current.Project!.PreviewUpdate();
                AppData.Current.AppStatus = Status.Edit;
            });
            Redo.Where(_ => AppData.Current.Project is not null)
                .Subscribe(_ =>
            {
                CommandManager.Redo();

                AppData.Current.Project!.PreviewUpdate();
                AppData.Current.AppStatus = Status.Edit;
            });
            CommandManager.CanUndoChange += (sender, e) => UndoIsEnabled.Value = CommandManager.CanUndo;
            CommandManager.CanRedoChange += (sender, e) => RedoIsEnabled.Value = CommandManager.CanRedo;

            CommandManager.Executed += Executed;

            #region Add, Remove
            SceneAdd.Where(_ => AppData.Current.Project is not null)
                .Subscribe(_ => SceneCreate?.Invoke(this, EventArgs.Empty));

            ClipAdd.Where(_ => AppData.Current.Project is not null)
                .Subscribe(_ => ClipCreate?.Invoke(this, EventArgs.Empty));

            EffectAdd.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project!.PreviewScene.SelectItem)
                .Where(c => c is not null)
                .Subscribe(c => EffectAddTo?.Invoke(this, c!));

            ClipRemove.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project!.PreviewScene.SelectItem)
                .Where(c => c is not null)
                .Subscribe(clip => clip!.Parent.RemoveClip(clip).Execute());
            #endregion

            #region Clipboard
            ClipboardCopy.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project!.PreviewScene.SelectItem)
                .Where(clip => clip is not null)
                .Subscribe(async clip =>
                {
                    await using var memory = new MemoryStream();
                    Serialize.SaveToStream(clip, memory, SerializeMode.Json);

                    var json = Encoding.Default.GetString(memory.ToArray());
                    Clipboard.SetText(json);
                });

            ClipboardCut.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project!.PreviewScene.SelectItem)
                .Where(clip => clip is not null)
                .Subscribe(async clip =>
                {
                    clip!.Parent.RemoveClip(clip).Execute();

                    await using var memory = new MemoryStream();
                    Serialize.SaveToStream(clip, memory, SerializeMode.Json);

                    var json = Encoding.Default.GetString(memory.ToArray());
                    Clipboard.SetText(json);
                });

            ClipboardPaste.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project!.PreviewScene.GetCreateTimeLineViewModel())
                .Subscribe(async timeline =>
                {
                    var mes = AppData.Current.Message;
                    var text = Clipboard.GetText();
                    var files = Clipboard.GetFileDropList();
                    var img = Clipboard.GetImage();
                    await using var memory = new MemoryStream();
                    memory.Write(Encoding.Default.GetBytes(text));

                    if (Serialize.LoadFromStream<ClipElement>(memory, SerializeMode.Json) is var clip && clip is not null)
                    {
                        var length = clip.Length;
                        clip.Start = timeline.Select_Frame;
                        clip.End = length + timeline.Select_Frame;

                        clip.Layer = timeline.Select_Layer;


                        if (!timeline.Scene.InRange(clip.Start, clip.End, clip.Layer))
                        {
                            mes?.Snackbar(MessageResources.ClipExistsInTheSpecifiedLocation);
                            Logger.LogInformation("{0} Start: {0} End: {1} Layer: {2}", MessageResources.ClipExistsInTheSpecifiedLocation, clip.Start, clip.End, clip.Layer);

                            return;
                        }

                        timeline.Scene.AddClip(clip).Execute();
                    }
                    else if (files is not null)
                    {
                        var start = timeline.Select_Frame;
                        var end = timeline.Select_Frame + 180;
                        var layer = timeline.Select_Layer;

                        if (!timeline.Scene.InRange(start, end, layer))
                        {
                            mes?.Snackbar(MessageResources.ClipExistsInTheSpecifiedLocation);
                            Logger.LogInformation("{0} Start: {0} End: {1} Layer: {2}", MessageResources.ClipExistsInTheSpecifiedLocation, start, end, layer);

                            return;
                        }

                        if (files.Count is > 0
                            && File.Exists(files[0]))
                        {
                            var file = files[0];

                            if (file is null) return;

                            var meta = FileTypeConvert(file);
                            timeline.Scene.AddClip(start, layer, meta, out var c).Execute();

                            var obj = c.Effect[0];
                            if (obj is VideoFile video)
                            {
                                video.File.Value = file;
                            }
                            else if (obj is ImageFile image)
                            {
                                image.File.Value = file;
                            }
                            else if (obj is Text txt)
                            {
                                using var reader = new StreamReader(file);
                                txt.Document.Value = reader.ReadToEnd();
                            }
                        }
                    }
                    else if (img is not null)
                    {
                        //Todo: 画像のコピペ
                    }
                });
            #endregion
        }

        public event EventHandler? SceneCreate;
        public event EventHandler? ClipCreate;
        public event EventHandler<ClipElement>? EffectAddTo;

        public ReactiveCommand Undo { get; } = new();
        public ReactiveCommand Redo { get; } = new();
        public ReactiveProperty<bool> UndoIsEnabled { get; } = new() { Value = CommandManager.CanUndo };
        public ReactiveProperty<bool> RedoIsEnabled { get; } = new() { Value = CommandManager.CanRedo };
        public ReactiveCollection<string> UnDoList { get; } = new();
        public ReactiveCollection<string> ReDoList { get; } = new();


        public ReactiveCommand ClipboardCut { get; } = new();
        public ReactiveCommand ClipboardCopy { get; } = new();
        public ReactiveCommand ClipboardPaste { get; } = new();


        public ReactiveCommand SceneAdd { get; } = new();
        public ReactiveCommand ClipAdd { get; } = new();
        public ReactiveCommand ClipRemove { get; } = new();
        public ReactiveCommand EffectAdd { get; } = new();

        private void Executed(object? sender, CommandType type)
        {
            try
            {
                if (type == CommandType.Do)
                {
                    //上を見てUnDoListに追加
                    ReDoList.Clear();

                    var command = CommandManager.UndoStack.Peek();

                    UnDoList.Insert(0, command.Name);

                    AppData.Current.Project!.PreviewUpdate();
                }
                else if (type == CommandType.Undo)
                {
                    //ReDoListに移動
                    if (UnDoList.Count == 0) return;

                    string name = UnDoList[0];
                    UnDoList.Remove(name);
                    ReDoList.Insert(0, name);

                }
                else if (type == CommandType.Redo)
                {
                    //UnDoListに移動
                    if (ReDoList.Count == 0) return;

                    string name = ReDoList[0];
                    ReDoList.Remove(name);
                    UnDoList.Insert(0, name);
                }
            }
            catch
            {

            }
        }
        public static ObjectMetadata FileTypeConvert(string file)
        {
            var ex = Path.GetExtension(file);
            if (ex is ".avi" or ".mp4")
            {
                return PrimitiveTypes.VideoMetadata;
            }
            else if (ex is ".jpg" or ".jpeg" or ".png" or ".bmp")
            {
                return PrimitiveTypes.ImageMetadata;
            }
            else if (ex is ".txt")
            {
                return PrimitiveTypes.TextMetadata;
            }

            return PrimitiveTypes.FigureMetadata;
        }
    }
}
