using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

using BEditor.Core;
using BEditor.Core.Command;
using BEditor.Core.Data;
using BEditor.Core.Extensions;
using BEditor.Core.Service;
using BEditor.Models.Extension;
using BEditor.ViewModels.CreateDialog;
using BEditor.Views;
using BEditor.Views.CreateDialog;

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
                CommandManager.Undo();

                AppData.Current.Project.PreviewUpdate();
                AppData.Current.AppStatus = Status.Edit;
            });
            Redo.Where(_ => AppData.Current.Project is not null)
                .Subscribe(_ =>
            {
                CommandManager.Redo();

                AppData.Current.Project.PreviewUpdate();
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
                .Select(_ => AppData.Current.Project.PreviewScene.SelectItem)
                .Where(c => c is not null)
                .Subscribe(c => EffectAddTo?.Invoke(this, c));

            ClipRemove.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project.PreviewScene.SelectItem)
                .Where(c => c is not null)
                .Subscribe(clip => clip.Parent.CreateRemoveCommand(clip).Execute());
            #endregion

            #region Clipboard
            ClipboardCopy.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project.PreviewScene.SelectItem)
                .Where(clip => clip is not null)
                .Subscribe(clip =>
                {
                    using var memory = new MemoryStream();
                    Serialize.SaveToStream(clip, memory, SerializeMode.Json);

                    var json = Encoding.Default.GetString(memory.ToArray());
                    Clipboard.SetText(json);
                });

            ClipboardCut.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project.PreviewScene.SelectItem)
                .Where(clip => clip is not null)
                .Subscribe(clip =>
                {
                    clip.Parent.CreateRemoveCommand(clip).Execute();

                    using var memory = new MemoryStream();
                    Serialize.SaveToStream(clip, memory, SerializeMode.Json);

                    var json = Encoding.Default.GetString(memory.ToArray());
                    Clipboard.SetText(json);
                });

            ClipboardPaste.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project.PreviewScene.GetCreateTimeLineViewModel())
                .Subscribe(timeline =>
                {
                    var text = Clipboard.GetText();
                    using var memory = new MemoryStream();
                    memory.Write(Encoding.Default.GetBytes(text));

                    var clip = Serialize.LoadFromStream<ClipData>(memory, SerializeMode.Json);

                    if (clip is null) return;

                    var length = clip.Length;
                    clip.Start = timeline.Select_Frame;
                    clip.End = length + timeline.Select_Frame;

                    clip.Layer = timeline.Select_Layer;


                    if (!timeline.Scene.InRange(clip.Start, clip.End, clip.Layer))
                    {
                        Message.Snackbar("指定した場所にクリップが存在しているため、新しいクリップを配置できません");

                        return;
                    }

                    timeline.Scene.CreateAddCommand(clip).Execute();
                });
            #endregion
        }

        public event EventHandler SceneCreate;
        public event EventHandler ClipCreate;
        public event EventHandler<ClipData> EffectAddTo;

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

        private void Executed(object sender, CommandType type)
        {
            try
            {
                if (type == CommandType.Do)
                {
                    //上を見てUnDoListに追加
                    ReDoList.Clear();

                    var command = CommandManager.UndoStack.Peek();

                    UnDoList.Insert(0, command.Name);

                    AppData.Current.Project.PreviewUpdate();
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
    }
}
