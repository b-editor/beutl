using System;
using System.Linq;
using System.Reactive.Linq;

using Avalonia.Media.Imaging;

using BEditor.Models;

using Reactive.Bindings;

namespace BEditor.ViewModels
{
    public class PreviewerViewModel
    {
        public PreviewerViewModel(IReadOnlyReactiveProperty<bool> isopened)
        {
            IsOpened = isopened;
            isopened.Subscribe(_ => PreviewImage.Value = null);

            MoveToTop.Select(_ => AppModel.Current.Project?.PreviewScene)
                .Where(s => s is not null)
                .Subscribe(s => s!.PreviewFrame = 0);

            MoveToPrevious.Select(_ => AppModel.Current.Project?.PreviewScene)
                .Where(s => s is not null)
                .Subscribe(s => s!.PreviewFrame--);

            PlayPause.Select(_ => AppModel.Current)
                .Where(app => app.Project is not null)
                .Subscribe(app =>
                {
                    if (app.AppStatus is Status.Playing)
                    {
                        app.AppStatus = Status.Edit;

                        app.Project!.PreviewScene.Player.Stop();
                    }
                    else
                    {
                        app.AppStatus = Status.Playing;

                        app.Project!.PreviewScene.Player.Play();
                    }
                });

            MoveToNext.Select(_ => AppModel.Current.Project?.PreviewScene)
                .Where(s => s is not null)
                .Subscribe(s => s!.PreviewFrame++);

            MoveToEnd.Select(_ => AppModel.Current.Project?.PreviewScene)
                .Where(s => s is not null)
                .Subscribe(s => s!.PreviewFrame = s.TotalFrame);
        }

        public ReactiveProperty<WriteableBitmap?> PreviewImage { get; } = new();

        public IReadOnlyReactiveProperty<bool> IsOpened { get; }

        public ReactiveCommand MoveToTop { get; } = new();

        public ReactiveCommand MoveToPrevious { get; } = new();

        public ReactiveCommand PlayPause { get; } = new();

        public ReactiveCommand MoveToNext { get; } = new();

        public ReactiveCommand MoveToEnd { get; } = new();

        public AppModel App { get; } = AppModel.Current;
    }
}