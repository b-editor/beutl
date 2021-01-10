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
using System.Threading;
using BEditor.ViewModels.CreateDialog;

namespace BEditor.ViewModels
{
    public sealed class MainWindowViewModel
    {
        public static MainWindowViewModel Current { get; } = new();

        public ReactiveProperty<WriteableBitmap> PreviewImage { get; } = new();
        public ReactiveProperty<Brush> MainWindowColor { get; } = new();

        public ReactiveCommand PreviewStart { get; } = new();
        public ReactiveCommand FrameNext { get; } = new();
        public ReactiveCommand FramePrevious { get; } = new();
        public ReactiveCommand FrameTop { get; } = new();
        public ReactiveCommand FrameEnd { get; } = new();

        public ReactiveCommand SendFeedback { get; } = new();
        public ReactiveCommand OpenThisRepository { get; } = new();

        public ReactiveCommand OpenProjectDirectory { get; } = new();
        public ReactiveCommand ConvertJson { get; } = new();

        public ReactiveCommand Shutdown { get; } = new();

        public ReactiveCommand DeleteCommand { get; } = new();
        public ReactiveCommand MemoryRelease { get; } = new();

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
            ProjectAddScene.Subscribe(() => new SceneCreateDialog().ShowDialog());
            ProjectAddClip.Subscribe(() =>
            {
                var dialog = new ClipCreateDialog()
                {
                    DataContext = new ClipCreateDialogViewModel()
                    {
                        Scene =
                        {
                            Value = AppData.Current.Project.PreviewScene
                        }
                    }
                };

                dialog.ShowDialog();
            });
            ProjectAddEffect.Subscribe(() =>
            {
                var dialog = new EffectAddDialog()
                {
                    DataContext = new EffectAddDialogViewModel()
                    {
                        Scene =
                        {
                            Value = AppData.Current.Project.PreviewScene
                        },
                        TargetClip =
                        {
                            Value = AppData.Current.Project.PreviewScene.SelectItem
                        }
                    }
                };

                dialog.ShowDialog();
            });
            ClipRemoveCommand.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project.PreviewScene.SelectItem)
                .Where(c => c is not null)
                .Subscribe(clip => clip.Parent.CreateRemoveCommand(clip).Execute());

            SettingShow.Subscribe(SettingShowCommand);

            PreviewStart.Subscribe(ProjectPreviewStartCommand);
            FrameNext.Where(_ => AppData.Current.Project is not null).Subscribe(_ => AppData.Current.Project.PreviewScene.PreviewFrame++);

            FramePrevious.Where(_ => AppData.Current.Project is not null).Subscribe(_ => AppData.Current.Project.PreviewScene.PreviewFrame--);

            FrameTop.Where(_ => AppData.Current.Project is not null).Subscribe(_ => AppData.Current.Project.PreviewScene.PreviewFrame = 0);

            FrameEnd.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project.PreviewScene)
                .Subscribe(scene => scene.PreviewFrame = scene.TotalFrame);

            UndoCommand.Subscribe(_ =>
            {
                CommandManager.Undo();

                AppData.Current.Project.PreviewUpdate();
                AppData.Current.AppStatus = Status.Edit;
            });
            RedoCommand.Subscribe(_ =>
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

            const string feedback = "https://github.com/indigo-san/BEditor/issues/new";
            const string repository = "https://github.com/indigo-san/BEditor/";

            SendFeedback.Subscribe(() => Process.Start(new ProcessStartInfo("cmd", $"/c start {feedback}") { CreateNoWindow = true }));
            OpenThisRepository.Subscribe(() => Process.Start(new ProcessStartInfo("cmd", $"/c start {repository}") { CreateNoWindow = true }));

            Shutdown.Subscribe(() => App.Current.Shutdown());

            OpenProjectDirectory
                .Where(_ => AppData.Current.Project is not null)
                .Subscribe(_ => Process.Start("explorer.exe", Path.GetDirectoryName(AppData.Current.Project.Filename)));

            ConvertJson.Where(_ => AppData.Current.Project is not null)
                .Subscribe(_ =>
                {
                    var temp = Path.GetTempFileName();
                    using var stream = new FileStream(temp, FileMode.Create);

                    if (!Serialize.SaveToStream(AppData.Current.Project, stream, SerializeMode.Json)) throw new Exception();

                    var p = Process.Start("notepad.exe", temp);

                    p.WaitForInputIdle();

                    File.Delete(temp);
                });

            DeleteCommand.Subscribe(() => CommandManager.Clear());
            MemoryRelease.Subscribe(() =>
            {
                var bytes = Environment.WorkingSet;

                GC.Collect();

                Message.Snackbar(((Environment.WorkingSet - bytes) / 10000000f).ToString() + "MB");
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
        public ReactiveCommand ProjectAddScene { get; } = new();
        public ReactiveCommand ProjectAddClip { get; } = new();
        public ReactiveCommand ProjectAddEffect { get; } = new();
        public ReactiveCommand ClipboardCopy { get; } = new();
        public ReactiveCommand ClipboardCut { get; } = new();
        public ReactiveCommand ClipboardPaste { get; } = new();
        public ReactiveCommand ClipRemoveCommand { get; } = new();

        public ReactiveProperty<bool> ProjectIsOpened { get; } = new() { Value = false };

        private static void ProjectSaveAsCommand()
            => AppData.Current.Project?.SaveAs();
        private static void ProjectSaveCommand()
            => AppData.Current.Project?.Save();
        public static void ProjectOpenCommand(string name)
        {
            try
            {
                AppData.Current.Project = new(name);
                AppData.Current.AppStatus = Status.Edit;

                Settings.Default.MostRecentlyUsedList.Remove(name);
                Settings.Default.MostRecentlyUsedList.Add(name);
            }
            catch
            {
                Debug.Assert(false);
                Message.Snackbar(string.Format(Resources.FailedToLoad, "Project"));
            }
        }
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
                try
                {
                    AppData.Current.Project = new(dialog.FileName);
                    AppData.Current.AppStatus = Status.Edit;

                    Settings.Default.MostRecentlyUsedList.Remove(dialog.FileName);
                    Settings.Default.MostRecentlyUsedList.Add(dialog.FileName);
                }
                catch
                {
                    Debug.Assert(false);
                    Message.Snackbar(string.Format(Resources.FailedToLoad, "Project"));
                }
            }
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
