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

using BEditor.NET.Data;
using BEditor.NET.Data.ObjectData;
using BEditor.NET.Extesions.ViewCommand;

using MaterialDesignThemes.Wpf;

using Microsoft.WindowsAPICodePack.Dialogs;

using Project = BEditor.NET.Data.ProjectData.Project;

namespace BEditor.ViewModels {
    public class MainWindowViewModel {
        public static MainWindowViewModel Current { get; } = new MainWindowViewModel();

        public DelegateProperty<Project> OpenProject { get; } = new DelegateProperty<Project>() { Value = Component.Current.Project };
        public DelegateProperty<ImageSource> PreviewImage { get; } = new DelegateProperty<ImageSource>();
        public DelegateProperty<Brush> MainWindowColor { get; } = new DelegateProperty<Brush>();

        public DelegateCommand PreviewFramePlus { get; } = new DelegateCommand();
        public DelegateCommand PreviewFrameMinus { get; } = new DelegateCommand();


        public SnackbarMessageQueue MessageQueue { get; } = new SnackbarMessageQueue();

        private MainWindowViewModel() {
            OutputImage.Subscribe(() => OutputImageCommand());
            OutputVideo.Subscribe(() => OutputVideoCommand());

            ProjectSaveAs.Subscribe(() => ProjectSaveAsCommand());
            ProjectSave.Subscribe(() => ProjectSaveCommand());
            ProjectOpen.Subscribe(() => ProjectOpenCommand());
            ProjectClose.Subscribe(() => ProjectCloseCommand());
            ProjectCreate.Subscribe(() => ProjectCreateCommand());

            SettingShow.Subscribe(() => SettingShowCommand());

            PreviewFramePlus.Subscribe(() => Component.Current.Project.PreviewScene.PreviewFrame++);
            PreviewFrameMinus.Subscribe(() => Component.Current.Project.PreviewScene.PreviewFrame--);

            #region UndoRedoRelect

            UndoSelect.Subscribe(() => {
                for (int i = 0; i < UndoSelectIndex.Value + 1; i++) {
                    UndoRedoManager.Undo();
                }

                Component.Current.Project.PreviewUpdate();
                Component.Current.Status = Status.Edit;
            });
            RedoSelect.Subscribe(() => {
                for (int i = 0; i < RedoSelectIndex.Value + 1; i++) {
                    UndoRedoManager.Redo();
                }

                Component.Current.Project.PreviewUpdate();
                Component.Current.Status = Status.Edit;
            });

            #endregion

            UndoCommand.Subscribe(() => {
                UndoRedoManager.Undo();

                Component.Current.Project.PreviewUpdate();
                Component.Current.Status = Status.Edit;
            });
            RedoCommand.Subscribe(() => {
                UndoRedoManager.Redo();

                Component.Current.Project.PreviewUpdate();
                Component.Current.Status = Status.Edit;
            });

            UndoRedoManager.CanUndoChange += (sender, e) => UndoIsEnabled.Value = UndoRedoManager.CanUndo;
            UndoRedoManager.CanRedoChange += (sender, e) => RedoIsEnabled.Value = UndoRedoManager.CanRedo;

            UndoRedoManager.DidEvent += DidEvent;

            Project.ProjectClosed += Project_Closed;
            Project.ProjectOpend += Project_Opend;
        }


        #region IOイベント

        private void Project_Opend(object sender, EventArgs e) {
            UndoRedoManager.Clear();

            ProjectIsOpened.Value = true;
        }

        private void Project_Closed(object sender, EventArgs e) {
            UndoRedoManager.Clear();

            ProjectIsOpened.Value = false;
        }

        #endregion


        #region OutputsCommands

        public DelegateCommand OutputImage { get; } = new DelegateCommand();
        public DelegateCommand OutputVideo { get; } = new DelegateCommand();


        public void OutputImageCommand() => ImageHelper.OutputImage();
        public void OutputVideoCommand() { }

        #endregion

        #region Project

        public DelegateCommand ProjectSaveAs { get; } = new DelegateCommand();
        public DelegateCommand ProjectSave { get; } = new DelegateCommand();
        public DelegateCommand ProjectOpen { get; } = new DelegateCommand();
        public DelegateCommand ProjectClose { get; } = new DelegateCommand();
        public DelegateCommand ProjectCreate { get; } = new DelegateCommand();

        public DelegateProperty<bool> ProjectIsOpened { get; } = new DelegateProperty<bool>() { Value = false };

        public void ProjectSaveAsCommand() => Project.SaveAs(Component.Current.Project);

        public void ProjectSaveCommand() => Project.Save(Component.Current.Project);

        public void ProjectOpenCommand() {
            var dialog = new CommonOpenFileDialog() {
                Filters = {
                    new CommonFileDialogFilter("プロジェクトファイル", "bedit"),
                    new CommonFileDialogFilter("バックアップファイル", "backup")
                },
                RestoreDirectory = true
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok) {
                var loading = new Loading();
                loading.IsIndeterminate.Value = true;

                NoneDialog dialog1 = new NoneDialog(loading);
                dialog1.Show();

                var project = Project.Open(dialog.FileName);
                if (project != null) {
                    Component.Current.Project = project;
                }
                else {
                    Message.Snackbar("読み込みに失敗しました");
                }

                dialog1.Close();
            }

            Debug.WriteLine("ProjectOpened");
        }

        public void ProjectCloseCommand() => Project.Close(Component.Current.Project);

        public void ProjectCreateCommand() => new CreateProjectWindow { Owner = App.Current.MainWindow }.ShowDialog();

        #endregion


        public DelegateCommand SettingShow { get; } = new DelegateCommand();

        public void SettingShowCommand() => new SettingsWindow() { Owner = App.Current.MainWindow }.ShowDialog();


        #region UndoRedoCommands

        public DelegateCommand UndoCommand { get; } = new DelegateCommand();
        public DelegateCommand UndoSelect { get; } = new DelegateCommand();
        public DelegateProperty<int> UndoSelectIndex { get; } = new DelegateProperty<int>();
        public DelegateProperty<bool> UndoIsEnabled { get; } = new DelegateProperty<bool>() { Value = UndoRedoManager.CanUndo };
        public DelegateCommand RedoCommand { get; } = new DelegateCommand();
        public DelegateCommand RedoSelect { get; } = new DelegateCommand();
        public DelegateProperty<int> RedoSelectIndex { get; } = new DelegateProperty<int>();
        public DelegateProperty<bool> RedoIsEnabled { get; } = new DelegateProperty<bool>() { Value = UndoRedoManager.CanRedo };


        public ObservableCollection<string> UnDoList { get; } = new();
        public ObservableCollection<string> ReDoList { get; } = new();

        private void DidEvent(object sender, CommandType type) {
            if (type == CommandType.Do) { //上を見てUnDoListに追加
                ReDoList.Clear();

                var command = UndoRedoManager.UndoStack.Peek();

                UnDoList.Insert(0, UndoRedoManager.CommandTypeDictionary[command.GetType()]);

                Component.Current.Project.PreviewUpdate();
            }
            else if (type == CommandType.Undo) { //ReDoListに移動
                if (UnDoList.Count == 0) {
                    return;
                }

                string name = UnDoList[0];
                UnDoList.Remove(name);
                ReDoList.Insert(0, name);

            }
            else if (type == CommandType.Redo) { //UnDoListに移動
                if (ReDoList.Count == 0) {
                    return;
                }

                string name = ReDoList[0];
                ReDoList.Remove(name);
                UnDoList.Insert(0, name);

            }
        }

        #endregion


        public ObservableCollection<ObjectData> AddedObjects { get; } = new ObservableCollection<ObjectData>() {
            new() { Name = "Test", Type = ClipType.Figure }
        };
    }
}
