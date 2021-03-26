using System;
using System.Diagnostics;
using System.IO;
using System.Reactive.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using BEditor;
using BEditor.Command;
using BEditor.Data;
using BEditor.Models;
using BEditor.ViewModels.SettingsControl;
using BEditor.Views.SettingsControl;
using BEditor.Views.ToolControl;

using MaterialDesignThemes.Wpf;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels
{
    public sealed class MainWindowViewModel
    {
        public static MainWindowViewModel Current { get; } = new();

        public ReactivePropertySlim<object?> ToolTipControl { get; } = new();
        public ReactivePropertySlim<WriteableBitmap?> PreviewImage { get; } = new();
        public ReactivePropertySlim<Brush> MainWindowColor { get; } = new();

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

        #region Statusbar
        public ReactivePropertySlim<bool> IsLoading { get; } = new(false);

        public ReactiveCommand OpenProjectDirectory { get; } = new();
        public ReactiveCommand ConvertJson { get; } = new();
        #endregion

        #region File(F)
        public ReactiveCommand Shutdown { get; } = new();
        #endregion

        #region Edit (E)

        public ReactiveCommand MoveFrame { get; } = new();

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
            FrameNext
                .Where(_ => AppData.Current.Project is not null)
                .Subscribe(_ => AppData.Current.Project!.PreviewScene.PreviewFrame++);

            FramePrevious
                .Where(_ => AppData.Current.Project is not null)
                .Subscribe(_ => AppData.Current.Project!.PreviewScene.PreviewFrame--);

            FrameTop
                .Where(_ => AppData.Current.Project is not null)
                .Subscribe(_ => AppData.Current.Project!.PreviewScene.PreviewFrame = 0);

            FrameEnd.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project!.PreviewScene)
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

            const string feedback = "https://github.com/b-editor/BEditor/issues/new";
            const string repository = "https://github.com/b-editor/BEditor/";

            SendFeedback.Subscribe(() => Process.Start(new ProcessStartInfo("cmd", $"/c start {feedback}") { CreateNoWindow = true }));
            OpenThisRepository.Subscribe(() => Process.Start(new ProcessStartInfo("cmd", $"/c start {repository}") { CreateNoWindow = true }));

            #endregion

            Shutdown.Subscribe(() => App.Current.Shutdown());

            #region Statusbar Right

            OpenProjectDirectory
                .Where(_ => AppData.Current.Project is not null)
                .Where(_ => AppData.Current.Project!.DirectoryName is not null)
                .Subscribe(_ => Process.Start("explorer.exe", AppData.Current.Project!.DirectoryName!));

            ConvertJson.Where(_ => AppData.Current.Project is not null)
                .Subscribe(async _ =>
                {
                    var temp = Path.GetTempFileName();
                    await using var stream = new FileStream(temp, FileMode.Create);

                    if (!await Serialize.SaveToStreamAsync(AppData.Current.Project, stream, SerializeMode.Json)) throw new Exception();

                    var p = Process.Start("notepad.exe", temp);

                    p.WaitForInputIdle();
                });

            #endregion

            #region Tool

            SettingShow.Subscribe(SettingShowCommand);
            DeleteCommand.Subscribe(() => CommandManager.Default.Clear());
            MemoryRelease.Subscribe(() =>
            {
                var bytes = Environment.WorkingSet;

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                AppData.Current.Message.Snackbar(((Environment.WorkingSet - bytes) / 10000000f).ToString() + "MB");
            });
            SceneSettingsCommand.Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project!.PreviewScene)
                .Subscribe(s =>
                {
                    var vm = new SceneSettingsViewModel(s);
                    var v = new SceneSettingsDialog()
                    {
                        DataContext = vm
                    };
                    v.ShowDialog();
                });

            #endregion

            MoveFrame.Where(_ => AppData.Current.Project is not null)
                .Subscribe(_ =>
            {
                ToolTipControl.Value = new MoveFrame();
            });
        }

        #region Model
        public static OutputModel Output => OutputModel.Current;
        public static ProjectModel ProjectModel => ProjectModel.Current;
        public static EditModel EditModel => EditModel.Current;
        #endregion

        #region イベント

        private void Project_Opend()
        {
            CommandManager.Default.Clear();

            ProjectIsOpened.Value = true;
            AppData.Current.Project!.Saved += (s, _) => AppData.Current.AppStatus = Status.Saved;
        }

        private void Project_Closed()
        {
            CommandManager.Default.Clear();
            PreviewImage.Value = null;

            ProjectIsOpened.Value = false;
        }

        #endregion


        #region Project

        public ReactivePropertySlim<bool> ProjectIsOpened { get; } = new() { Value = false };

        private void ProjectPlayPauseCommand(object _)
        {
            if (AppData.Current.AppStatus is Status.Playing)
            {
                AppData.Current.AppStatus = Status.Edit;
                AppData.Current.Project!.PreviewScene.Player.Stop();
                AppData.Current.IsNotPlaying = true;
            }
            else
            {
                AppData.Current.AppStatus = Status.Playing;

                AppData.Current.Project!.PreviewScene.Player.Play();
                AppData.Current.IsNotPlaying = false;
            }
        }

        #endregion



        private static void SettingShowCommand()
        {
            new SettingsWindow() { Owner = App.Current.MainWindow }.ShowDialog();
        }
    }
}
