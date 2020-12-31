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

namespace BEditor.ViewModels
{
    public sealed class MainWindowViewModel
    {
        public static MainWindowViewModel Current { get; } = new();

        public ReactiveProperty<WriteableBitmap> PreviewImage { get; } = new();
        public ReactiveProperty<Brush> MainWindowColor { get; } = new();

        public ReactiveCommand PreviewFramePlus { get; } = new();
        public ReactiveCommand PreviewFrameMinus { get; } = new();


        public SnackbarMessageQueue MessageQueue { get; } = new();

        private MainWindowViewModel()
        {
            OutputImage.Subscribe(() => OutputImageCommand());
            OutputVideo.Subscribe(() => OutputVideoCommand());

            ProjectSaveAs.Subscribe(() => ProjectSaveAsCommand());
            ProjectSave.Subscribe(() => ProjectSaveCommand());
            ProjectOpen.Subscribe(() => ProjectOpenCommand());
            ProjectClose.Subscribe(() => ProjectCloseCommand());
            ProjectCreate.Subscribe(() => ProjectCreateCommand());

            SettingShow.Subscribe(() => SettingShowCommand());

            PreviewFramePlus.Subscribe(() => AppData.Current.Project.PreviewScene.PreviewFrame++);
            PreviewFrameMinus.Subscribe(() => AppData.Current.Project.PreviewScene.PreviewFrame--);

            #region UndoRedoRelect

            UndoSelect.Subscribe(() =>
            {
                for (int i = 0; i < UndoSelectIndex.Value + 1; i++)
                {
                    CommandManager.Undo();
                }

                AppData.Current.Project.PreviewUpdate();
                AppData.Current.AppStatus = Status.Edit;
            });
            RedoSelect.Subscribe(() =>
            {
                for (int i = 0; i < RedoSelectIndex.Value + 1; i++)
                {
                    CommandManager.Redo();
                }

                AppData.Current.Project.PreviewUpdate();
                AppData.Current.AppStatus = Status.Edit;
            });

            #endregion

            UndoCommand.Subscribe(() =>
            {
                CommandManager.Undo();

                AppData.Current.Project.PreviewUpdate();
                AppData.Current.AppStatus = Status.Edit;
            });
            RedoCommand.Subscribe(() =>
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


        public void OutputImageCommand() => ImageHelper.OutputImage();
        public void OutputVideoCommand() => ImageHelper.OutputVideo();

        #endregion

        #region Project

        public ReactiveCommand ProjectSaveAs { get; } = new();
        public ReactiveCommand ProjectSave { get; } = new();
        public ReactiveCommand ProjectOpen { get; } = new();
        public ReactiveCommand ProjectClose { get; } = new();
        public ReactiveCommand ProjectCreate { get; } = new();

        public ReactiveProperty<bool> ProjectIsOpened { get; } = new() { Value = false };

        private static void ProjectSaveAsCommand() => AppData.Current.Project?.SaveAs();

        private static void ProjectSaveCommand() => AppData.Current.Project?.Save();

        private static void ProjectOpenCommand()
        {
            var dialog = new CommonOpenFileDialog()
            {
                Filters = {
                    new CommonFileDialogFilter("プロジェクトファイル", "bedit"),
                    new CommonFileDialogFilter("バックアップファイル", "backup")
                },
                RestoreDirectory = true
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                App.Current.Dispatcher.Invoke(() =>
                {
                    var loading = new Loading();
                    loading.IsIndeterminate.Value = true;

                    NoneDialog dialog1 = new NoneDialog(loading);
                    dialog1.Show();

                    try
                    {
                        AppData.Current.Project = new Project(dialog.FileName);
                        AppData.Current.AppStatus = Status.Edit;
                    }
                    catch
                    {
                        Message.Snackbar("読み込みに失敗しました");
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

        private static void ProjectCreateCommand() => new ProjectCreateDialog { Owner = App.Current.MainWindow }.ShowDialog();

        #endregion


        public ReactiveCommand SettingShow { get; } = new();

        public void SettingShowCommand() => new SettingsWindow() { Owner = App.Current.MainWindow }.ShowDialog();


        #region UndoRedoCommands

        public ReactiveCommand UndoCommand { get; } = new();
        public ReactiveCommand UndoSelect { get; } = new();
        public ReactiveProperty<int> UndoSelectIndex { get; } = new();
        public ReactiveProperty<bool> UndoIsEnabled { get; } = new() { Value = CommandManager.CanUndo };
        public ReactiveCommand RedoCommand { get; } = new();
        public ReactiveCommand RedoSelect { get; } = new();
        public ReactiveProperty<int> RedoSelectIndex { get; } = new();
        public ReactiveProperty<bool> RedoIsEnabled { get; } = new() { Value = CommandManager.CanRedo };


        public ObservableCollection<string> UnDoList { get; } = new();
        public ObservableCollection<string> ReDoList { get; } = new();

        private void Executed(object sender, CommandType type)
        {
            try
            {
                if (type == CommandType.Do)
                { //上を見てUnDoListに追加
                    ReDoList.Clear();

                    var command = CommandManager.UndoStack.Peek();

                    UnDoList.Insert(0, command.Name);

                    AppData.Current.Project.PreviewUpdate();
                }
                else if (type == CommandType.Undo)
                { //ReDoListに移動
                    if (UnDoList.Count == 0)
                    {
                        return;
                    }

                    string name = UnDoList[0];
                    UnDoList.Remove(name);
                    ReDoList.Insert(0, name);

                }
                else if (type == CommandType.Redo)
                { //UnDoListに移動
                    if (ReDoList.Count == 0)
                    {
                        return;
                    }

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


        public ObservableCollection<ObjectMetadata> AddedObjects { get; } = new()
        {
            new() { Name = "Test", Type = ClipType.Figure }
        };
    }
}
