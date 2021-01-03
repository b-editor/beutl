using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Media;

using BEditor.Models;
using BEditor.Models.Extension;
using BEditor.Views;
using BEditor.Views.MessageContent;
using BEditor.Views.SettingsControl;

using BEditor.Core.Data;
using BEditor.Core.Data.Control;
using BEditor.Core.Extensions.ViewCommand;

using MaterialDesignThemes.Wpf;

using Microsoft.WindowsAPICodePack.Dialogs;

using Project = BEditor.Core.Data.Project;
using BEditor.Core.Service;
using BEditor.Core.Command;
using System.Reactive.Linq;
using Reactive.Bindings;
using BEditor.Views.CreateDialog;
using System.Windows.Media.Imaging;
using Reactive.Bindings.Extensions;
using BEditor.Core.Properties;
using System.Runtime.InteropServices;
using System.Windows;
using BEditor.Core;
using System.IO;
using System.Text;

namespace BEditor.ViewModels
{
    public sealed class MainWindowViewModel
    {
        public static MainWindowViewModel Current { get; } = new();

        public ReactiveProperty<WriteableBitmap> PreviewImage { get; } = new();
        public ReactiveProperty<Brush> MainWindowColor { get; } = new();

        public ReactiveCommand PreviewStart { get; } = new();
        public ReactiveCommand FramePlus { get; } = new();
        public ReactiveCommand FrameMinus { get; } = new();
        public ReactiveCommand FrameStart { get; } = new();
        public ReactiveCommand FrameEnd { get; } = new();


        public SnackbarMessageQueue MessageQueue { get; } = new();

        private MainWindowViewModel()
        {
            OutputImage.Where(_ => AppData.Current.Project is not null)
                .Subscribe(OutputImageCommand);
            OutputVideo.Where(_ => AppData.Current.Project is not null)
                .Subscribe(OutputVideoCommand);

            ProjectSaveAs.Subscribe(ProjectSaveAsCommand);
            ProjectSave.Subscribe(ProjectSaveCommand);
            ProjectOpen.Subscribe(ProjectOpenCommand);
            ProjectClose.Subscribe(ProjectCloseCommand);
            ProjectCreate.Subscribe(ProjectCreateCommand);
            ClipRemoveCommand.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project.PreviewScene.SelectItem)
                .Subscribe(clip => clip.Parent.CreateRemoveCommand(clip));

            SettingShow.Subscribe(SettingShowCommand);

            PreviewStart.Subscribe(ProjectPreviewStartCommand);
            FramePlus.Where(_ => AppData.Current.Project is not null)
                .Subscribe(_ => AppData.Current.Project.PreviewScene.PreviewFrame++);

            FrameMinus.Where(_ => AppData.Current.Project is not null)
                .Subscribe(_ => AppData.Current.Project.PreviewScene.PreviewFrame--);

            FrameStart.Where(_ => AppData.Current.Project is not null)
                .Subscribe(_ => AppData.Current.Project.PreviewScene.PreviewFrame = 0);

            FrameEnd.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project.PreviewScene)
                .Subscribe(scene => scene.PreviewFrame = scene.TotalFrame);

            UndoCommand.Where(_ => UndoIsEnabled.Value)
                .Subscribe(_ =>
            {
                CommandManager.Undo();

                AppData.Current.Project.PreviewUpdate();
                AppData.Current.AppStatus = Status.Edit;
            });
            RedoCommand.Where(_ => RedoIsEnabled.Value)
                .Subscribe(_ =>
            {
                CommandManager.Redo();

                AppData.Current.Project.PreviewUpdate();
                AppData.Current.AppStatus = Status.Edit;
            });

            CommandManager.CanUndoChange += (sender, e) => UndoIsEnabled.Value = CommandManager.CanUndo;
            CommandManager.CanRedoChange += (sender, e) => RedoIsEnabled.Value = CommandManager.CanRedo;

            CommandManager.Executed += Executed;

            AppData.Current
                .ObserveProperty(app => app.Project)
                .Subscribe(_ =>
                {
                    if (AppData.Current.Project is null)
                    {
                        Project_Closed();
                    }
                    else
                    {
                        Project_Opend();
                    }
                });

            ClipboardCopy.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project.PreviewScene.SelectItem)
                .Subscribe(clip =>
                {
                    using var memory = new MemoryStream();
                    Serialize.SaveToStream(clip, memory, SerializeMode.Json);

                    var json = Encoding.Default.GetString(memory.ToArray());
                    Clipboard.SetText(json);
                });

            ClipboardCut.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project.PreviewScene.SelectItem)
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

                    var length = clip.Length;
                    clip.Start = timeline.Select_Frame;
                    clip.End = length + timeline.Select_Frame;

                    clip.Layer = timeline.Select_Layer;

                    timeline.Scene.CreateAddCommand(clip).Execute();
                });
        }


        #region IOイベント

        private void Project_Opend()
        {
            CommandManager.Clear();

            ProjectIsOpened.Value = true;
            AppData.Current.Project.Saved += (_, _) => AppData.Current.AppStatus = Status.Saved;
        }

        private void Project_Closed()
        {
            CommandManager.Clear();

            ProjectIsOpened.Value = false;
        }

        #endregion


        #region OutputsCommands

        public ReactiveCommand OutputImage { get; } = new();
        public ReactiveCommand OutputVideo { get; } = new();


        private static void OutputImageCommand(object _) => ImageHelper.OutputImage();
        private static void OutputVideoCommand(object _) => ImageHelper.OutputVideo();

        #endregion

        #region Project

        public ReactiveCommand ProjectSaveAs { get; } = new();
        public ReactiveCommand ProjectSave { get; } = new();
        public ReactiveCommand ProjectOpen { get; } = new();
        public ReactiveCommand ProjectClose { get; } = new();
        public ReactiveCommand ProjectCreate { get; } = new();
        public ReactiveCommand ClipboardCopy { get; } = new();
        public ReactiveCommand ClipboardCut { get; } = new();
        public ReactiveCommand ClipboardPaste { get; } = new();
        public ReactiveCommand ClipRemoveCommand { get; } = new();

        public ReactiveProperty<bool> ProjectIsOpened { get; } = new() { Value = false };

        private static void ProjectSaveAsCommand()
            => AppData.Current.Project?.SaveAs();
        private static void ProjectSaveCommand()
            => AppData.Current.Project?.Save();
        private static void ProjectOpenCommand()
        {
            var dialog = new CommonOpenFileDialog()
            {
                Filters =
                {
                    new("プロジェクトファイル", "bedit"),
                    new("バックアップファイル", "backup")
                },
                RestoreDirectory = true
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    var loading = new Loading();
                    loading.IsIndeterminate.Value = true;

                    var dialog1 = new NoneDialog(loading);
                    dialog1.Show();

                    try
                    {
                        AppData.Current.Project = new(dialog.FileName);
                        AppData.Current.AppStatus = Status.Edit;
                    }
                    catch
                    {
                        Message.Snackbar(string.Format(Resources.FailedToLoad, "Project"));
                    }

                    dialog1.Close();
                });
            }

            Debug.WriteLine("ProjectOpened");
        }
        private static void ProjectCloseCommand()
        {
            AppData.Current.Project?.Dispose();
            AppData.Current.Project = null;
            AppData.Current.AppStatus = Status.Idle;
        }
        private static void ProjectCreateCommand()
            => new ProjectCreateDialog { Owner = App.Current.MainWindow }.ShowDialog();
        private void ProjectPreviewStartCommand()
        {
            if (AppData.Current.AppStatus is Status.Playing)
            {
                AppData.Current.AppStatus = Status.Edit;
                AppData.Current.Project.PreviewScene.Player.Stop();
            }
            else
            {
                AppData.Current.AppStatus = Status.Playing;

                AppData.Current.Project.PreviewScene.Player.Play();
            }
        }

        #endregion


        public ReactiveCommand SettingShow { get; } = new();

        private static void SettingShowCommand() => new SettingsWindow() { Owner = App.Current.MainWindow }.ShowDialog();


        #region UndoRedoCommands

        public ReactiveCommand UndoCommand { get; } = new();
        public ReactiveProperty<bool> UndoIsEnabled { get; } = new() { Value = CommandManager.CanUndo };
        public ReactiveCommand RedoCommand { get; } = new();
        public ReactiveProperty<bool> RedoIsEnabled { get; } = new() { Value = CommandManager.CanRedo };


        public ReactiveCollection<string> UnDoList { get; } = new();
        public ReactiveCollection<string> ReDoList { get; } = new();

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

        #endregion
    }
}
