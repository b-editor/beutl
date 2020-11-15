using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Media;

using BEditor.Models;
using BEditor.Models.Extension;
using BEditor.ViewModels.Helper;
using BEditor.Views;
using BEditor.Views.MessageContent;
using BEditor.Views.SettingsControl;

using BEditor.Core.Data;
using BEditor.Core.Data.ObjectData;
using BEditor.Core.Extensions.ViewCommand;

using MaterialDesignThemes.Wpf;

using Microsoft.WindowsAPICodePack.Dialogs;

using Project = BEditor.Core.Data.ProjectData.Project;

namespace BEditor.ViewModels
{
    public sealed class MainWindowViewModel
    {
        public static MainWindowViewModel Current { get; } = new();

        public DelegateProperty<Project> OpenProject { get; } = new() { Value = AppData.Current.Project };
        public DelegateProperty<ImageSource> PreviewImage { get; } = new();
        public DelegateProperty<Brush> MainWindowColor { get; } = new();

        public DelegateCommand PreviewFramePlus { get; } = new();
        public DelegateCommand PreviewFrameMinus { get; } = new();


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
                    UndoRedoManager.Undo();
                }

                AppData.Current.Project.PreviewUpdate();
                AppData.Current.AppStatus = Status.Edit;
            });
            RedoSelect.Subscribe(() =>
            {
                for (int i = 0; i < RedoSelectIndex.Value + 1; i++)
                {
                    UndoRedoManager.Redo();
                }

                AppData.Current.Project.PreviewUpdate();
                AppData.Current.AppStatus = Status.Edit;
            });

            #endregion

            UndoCommand.Subscribe(() =>
            {
                UndoRedoManager.Undo();

                AppData.Current.Project.PreviewUpdate();
                AppData.Current.AppStatus = Status.Edit;
            });
            RedoCommand.Subscribe(() =>
            {
                UndoRedoManager.Redo();

                AppData.Current.Project.PreviewUpdate();
                AppData.Current.AppStatus = Status.Edit;
            });

            UndoRedoManager.CanUndoChange += (sender, e) => UndoIsEnabled.Value = UndoRedoManager.CanUndo;
            UndoRedoManager.CanRedoChange += (sender, e) => RedoIsEnabled.Value = UndoRedoManager.CanRedo;

            UndoRedoManager.DidEvent += DidEvent;

            AppData.Current.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AppData.Project))
                {
                    if (AppData.Current.Project is null) Project_Closed();
                    else Project_Opend();
                }
            };
        }


        #region IOイベント

        private void Project_Opend()
        {
            UndoRedoManager.Clear();

            ProjectIsOpened.Value = true;
        }

        private void Project_Closed()
        {
            UndoRedoManager.Clear();

            ProjectIsOpened.Value = false;
        }

        #endregion


        #region OutputsCommands

        public DelegateCommand OutputImage { get; } = new();
        public DelegateCommand OutputVideo { get; } = new();


        public void OutputImageCommand() => ImageHelper.OutputImage();
        public void OutputVideoCommand() { }

        #endregion

        #region Project

        public DelegateCommand ProjectSaveAs { get; } = new();
        public DelegateCommand ProjectSave { get; } = new();
        public DelegateCommand ProjectOpen { get; } = new();
        public DelegateCommand ProjectClose { get; } = new();
        public DelegateCommand ProjectCreate { get; } = new();

        public DelegateProperty<bool> ProjectIsOpened { get; } = new() { Value = false };

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

        private static void ProjectCreateCommand() => new CreateProjectWindow { Owner = App.Current.MainWindow }.ShowDialog();

        #endregion


        public DelegateCommand SettingShow { get; } = new();

        public void SettingShowCommand() => new SettingsWindow() { Owner = App.Current.MainWindow }.ShowDialog();


        #region UndoRedoCommands

        public DelegateCommand UndoCommand { get; } = new();
        public DelegateCommand UndoSelect { get; } = new();
        public DelegateProperty<int> UndoSelectIndex { get; } = new();
        public DelegateProperty<bool> UndoIsEnabled { get; } = new() { Value = UndoRedoManager.CanUndo };
        public DelegateCommand RedoCommand { get; } = new();
        public DelegateCommand RedoSelect { get; } = new();
        public DelegateProperty<int> RedoSelectIndex { get; } = new();
        public DelegateProperty<bool> RedoIsEnabled { get; } = new() { Value = UndoRedoManager.CanRedo };


        public ObservableCollection<string> UnDoList { get; } = new();
        public ObservableCollection<string> ReDoList { get; } = new();

        private void DidEvent(object sender, CommandType type)
        {
            try
            {
                if (type == CommandType.Do)
                { //上を見てUnDoListに追加
                    ReDoList.Clear();

                    var command = UndoRedoManager.UndoStack.Peek();

                    UnDoList.Insert(0, UndoRedoManager.CommandTypeDictionary[command.GetType()]);

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


        public ObservableCollection<ObjectData> AddedObjects { get; } = new()
        {
            new() { Name = "Test", Type = ClipType.Figure }
        };
    }
}
