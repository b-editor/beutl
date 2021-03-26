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
using BEditor.Drawing;
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

        private EditModel()
        {
            Undo.Where(_ => AppData.Current.Project is not null)
                .Subscribe(_ =>
            {
                CommandManager.Default.Undo();

                AppData.Current.Project!.PreviewUpdate();
                AppData.Current.AppStatus = Status.Edit;
            });
            Redo.Where(_ => AppData.Current.Project is not null)
                .Subscribe(_ =>
            {
                CommandManager.Default.Redo();

                AppData.Current.Project!.PreviewUpdate();
                AppData.Current.AppStatus = Status.Edit;
            });

            CommandManager.Default.Executed += Executed;

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
                    await Serialize.SaveToStreamAsync(clip!, memory, SerializeMode.Json);

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
                    await Serialize.SaveToStreamAsync(clip, memory, SerializeMode.Json);

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
                    var bmpSrc = Clipboard.GetImage();
                    await using var memory = new MemoryStream();
                    await memory.WriteAsync(Encoding.Default.GetBytes(text));

                    if (await Serialize.LoadFromStreamAsync<ClipElement>(memory, SerializeMode.Json) is var clip && clip is not null)
                    {
                        var length = clip.Length;
                        clip.Start = timeline.Select_Frame;
                        clip.End = length + timeline.Select_Frame;

                        clip.Layer = timeline.Select_Layer;


                        if (!timeline.Scene.InRange(clip.Start, clip.End, clip.Layer))
                        {
                            mes?.Snackbar(MessageResources.ClipExistsInTheSpecifiedLocation);
                            App.Logger.LogInformation("{0} Start: {0} End: {1} Layer: {2}", MessageResources.ClipExistsInTheSpecifiedLocation, clip.Start, clip.End, clip.Layer);

                            return;
                        }

                        timeline.Scene.AddClip(clip).Execute();
                    }
                    else if (files is not null && files.Count is > 0)
                    {
                        var start = timeline.Select_Frame;
                        var end = timeline.Select_Frame + 180;
                        var layer = timeline.Select_Layer;

                        if (!timeline.Scene.InRange(start, end, layer))
                        {
                            mes?.Snackbar(MessageResources.ClipExistsInTheSpecifiedLocation);
                            App.Logger.LogInformation("{0} Start: {0} End: {1} Layer: {2}", MessageResources.ClipExistsInTheSpecifiedLocation, start, end, layer);

                            return;
                        }

                        if (File.Exists(files[0]))
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
                                txt.Document.Value = await reader.ReadToEndAsync();
                            }
                        }
                    }
                    else if (bmpSrc is not null)
                    {
                        var start = timeline.Select_Frame;
                        var end = timeline.Select_Frame + 180;
                        var layer = timeline.Select_Layer;

                        if (!timeline.Scene.InRange(start, end, layer))
                        {
                            mes?.Snackbar(MessageResources.ClipExistsInTheSpecifiedLocation);
                            App.Logger.LogInformation("{0} Start: {0} End: {1} Layer: {2}", MessageResources.ClipExistsInTheSpecifiedLocation, start, end, layer);

                            return;
                        }

                        timeline.Scene.AddClip(start, layer, PrimitiveTypes.ImageMetadata, out var c).Execute();
                        var ef = (ImageFile)c.Effect[0];
                        var filename = Path.Combine(c.Parent.Parent.DirectoryName, c.ToString("#") + ".png");

                        using var img = bmpSrc.ToImage();
                        img.Encode(filename);

                        ef.File.ChangeFile(filename).Execute();
                        ef.File.Mode = Data.Property.FilePathType.FromProject;
                    }
                });
            #endregion
        }

        public event EventHandler? SceneCreate;
        public event EventHandler? ClipCreate;
        public event EventHandler<ClipElement>? EffectAddTo;

        public ReactiveCommand Undo { get; } = new();
        public ReactiveCommand Redo { get; } = new();
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
                if (type is CommandType.Do)
                {
                    // 上を見てUnDoListに追加
                    ReDoList.Clear();

                    var command = CommandManager.Default.UndoStack.Peek();

                    UnDoList.Insert(0, command.Name);

                    AppData.Current.Project?.PreviewUpdate();
                }
                else if (type is CommandType.Undo)
                {
                    // ReDoListに移動
                    if (UnDoList.Count is 0) return;

                    string name = UnDoList[0];
                    UnDoList.Remove(name);
                    ReDoList.Insert(0, name);

                }
                else if (type is CommandType.Redo)
                {
                    // UnDoListに移動
                    if (ReDoList.Count is 0) return;

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
