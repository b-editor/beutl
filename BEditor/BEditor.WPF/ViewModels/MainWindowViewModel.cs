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
using BEditor.Core.Extensions;

using MaterialDesignThemes.Wpf;

using Microsoft.WindowsAPICodePack.Dialogs;

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
using BEditor.ViewModels.SettingsControl;

namespace BEditor.ViewModels
{
    public sealed class MainWindowViewModel
    {
        public static MainWindowViewModel Current { get; } = new();

        public ReactiveProperty<WriteableBitmap> PreviewImage { get; } = new();
        public ReactiveProperty<Brush> MainWindowColor { get; } = new();

        #region Seekbar
        public ReactiveCommand PlayPause { get; } = new();
        public ReactiveCommand FrameNext { get; } = new();
        public ReactiveCommand FramePrevious { get; } = new();
        public ReactiveCommand FrameTop { get; } = new();
        public ReactiveCommand FrameEnd { get; } = new();
        #endregion

        #region Help(H)
        public ReactiveCommand SendFeedback { get; } = new();
        public ReactiveCommand OpenThisRepository { get; } = new();
        #endregion

        #region Statusbar Right
        public ReactiveCommand OpenProjectDirectory { get; } = new();
        public ReactiveCommand ConvertJson { get; } = new();
        #endregion

        #region File(F)
        public ReactiveCommand Shutdown { get; } = new();
        #endregion

        #region Tool(T)
        public ReactiveCommand SettingShow { get; } = new();

        public ReactiveCommand DeleteCommand { get; } = new();
        public ReactiveCommand MemoryRelease { get; } = new();
        #endregion

        public ReactiveCommand SceneSettingsCommand { get; } = new();

        public SnackbarMessageQueue MessageQueue { get; } = new();

        private MainWindowViewModel()
        {
            #region Seekbar

            PlayPause
                .Where(_ => AppData.Current.Project is not null)
                .Subscribe(ProjectPlayPauseCommand);
            FrameNext.Where(_ => AppData.Current.Project is not null).Subscribe(_ => AppData.Current.Project.PreviewScene.PreviewFrame++);

            FramePrevious.Where(_ => AppData.Current.Project is not null).Subscribe(_ => AppData.Current.Project.PreviewScene.PreviewFrame--);

            FrameTop.Where(_ => AppData.Current.Project is not null).Subscribe(_ => AppData.Current.Project.PreviewScene.PreviewFrame = 0);

            FrameEnd.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project.PreviewScene)
                .Subscribe(scene => scene.PreviewFrame = scene.TotalFrame);

            #endregion

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


            #region Help

            const string feedback = "https://github.com/indigo-san/BEditor/issues/new";
            const string repository = "https://github.com/indigo-san/BEditor/";

            SendFeedback.Subscribe(() => Process.Start(new ProcessStartInfo("cmd", $"/c start {feedback}") { CreateNoWindow = true }));
            OpenThisRepository.Subscribe(() => Process.Start(new ProcessStartInfo("cmd", $"/c start {repository}") { CreateNoWindow = true }));

            #endregion

            Shutdown.Subscribe(() => App.Current.Shutdown());

            #region Statusbar Right

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

            #endregion

            #region Tool

            SettingShow.Subscribe(SettingShowCommand);
            DeleteCommand.Subscribe(() => CommandManager.Clear());
            MemoryRelease.Subscribe(() =>
            {
                var bytes = Environment.WorkingSet;

                GC.Collect();

                Message.Snackbar(((Environment.WorkingSet - bytes) / 10000000f).ToString() + "MB");
            });
            SceneSettingsCommand.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project.PreviewScene)
                .Subscribe(s =>
                {
                    var vm = new SceneSettingsViewModel(s);
                    var v = new Views.SettingsControls.SceneSettingsDialog()
                    {
                        DataContext = vm
                    };
                    v.ShowDialog();
                });

            #endregion
        }

        #region Model
        public OutputModel Output => OutputModel.Current;
        public ProjectModel ProjectModel => ProjectModel.Current;
        public EditModel EditModel => EditModel.Current;
        #endregion

        #region イベント

        private void Project_Opend()
        {
            CommandManager.Clear();

            ProjectIsOpened.Value = true;
            AppData.Current.Project.Saved += (_, _) => AppData.Current.AppStatus = Status.Saved;
        }

        private void Project_Closed()
        {
            AppData.Current.Project?.Unloaded();
            CommandManager.Clear();
            PreviewImage.Value = null;

            ProjectIsOpened.Value = false;
        }

        #endregion


        #region Project

        public ReactiveProperty<bool> ProjectIsOpened { get; } = new() { Value = false };

        private void ProjectPlayPauseCommand(object _)
        {
            if (AppData.Current.AppStatus is Status.Playing)
            {
                AppData.Current.AppStatus = Status.Edit;
                AppData.Current.Project.PreviewScene.Player.Stop();
                AppData.Current.IsNotPlaying = true;
            }
            else
            {
                AppData.Current.AppStatus = Status.Playing;

                AppData.Current.Project.PreviewScene.Player.Play();
                AppData.Current.IsNotPlaying = false;
            }
        }

        #endregion



        private static void SettingShowCommand() => new SettingsWindow() { Owner = App.Current.MainWindow }.ShowDialog();
    }
}
