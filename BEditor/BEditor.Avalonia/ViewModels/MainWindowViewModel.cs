using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Media.Imaging;

using BEditor.Core.Command;
using BEditor.Core.Data;
using BEditor.Core.Service;
using BEditor.Models;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels
{
    public class MainWindowViewModel
    {
        private MainWindowViewModel()
        {
            #region Seekbar

            PlayPause
                .Where(_ => AppData.Current.Project is not null)
                .Select(_ => AppData.Current.Project!.PreviewScene)
                .Subscribe(ProjectPlayPauseCommand);
            FrameNext.Where(_ => AppData.Current.Project is not null)
                .Subscribe(_ => AppData.Current.Project!.PreviewScene.PreviewFrame++);

            FramePrevious.Where(_ => AppData.Current.Project is not null)
                .Subscribe(_ => AppData.Current.Project!.PreviewScene.PreviewFrame--);

            FrameTop.Where(_ => AppData.Current.Project is not null)
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


        }

        public static MainWindowViewModel Current { get; } = new();
        public ReactiveProperty<bool> ProjectIsOpened { get; } = new() { Value = false };
        public ReactiveProperty<WriteableBitmap?> PreviewImage { get; } = new();

        #region Seekbar
        public ReactiveCommand PlayPause { get; } = new();
        public ReactiveCommand FrameNext { get; } = new();
        public ReactiveCommand FramePrevious { get; } = new();
        public ReactiveCommand FrameTop { get; } = new();
        public ReactiveCommand FrameEnd { get; } = new();
        #endregion

        #region Model
        //public static OutputModel Output => OutputModel.Current;
        public static ProjectModel ProjectModel => ProjectModel.Current;
        //public static EditModel EditModel => EditModel.Current;
        #endregion

        #region File(F)

        #endregion


        private void ProjectPlayPauseCommand(Scene scene)
        {
            if (AppData.Current.AppStatus is Status.Playing)
            {
                AppData.Current.AppStatus = Status.Edit;
                scene.Player.Stop();
                AppData.Current.IsNotPlaying = true;
            }
            else
            {
                AppData.Current.AppStatus = Status.Playing;

                scene.Player.Play();
                AppData.Current.IsNotPlaying = false;
            }
        }

        #region イベント

        private void Project_Opend()
        {
            CommandManager.Clear();

            ProjectIsOpened.Value = true;
            AppData.Current.Project!.Saved += (_, _) => AppData.Current.AppStatus = Status.Saved;
        }

        private void Project_Closed()
        {
            CommandManager.Clear();
            PreviewImage.Value = null;

            ProjectIsOpened.Value = false;
        }

        #endregion
    }
}
