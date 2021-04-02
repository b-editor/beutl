using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Media;

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

        public ReactiveProperty<IImage?> PreviewImage { get; } = new();
        public IReadOnlyReactiveProperty<bool> IsOpened { get; }
        public ReactiveCommand MoveToTop { get; } = new();
        public ReactiveCommand MoveToPrevious { get; } = new();
        public ReactiveCommand PlayPause { get; } = new();
        public ReactiveCommand MoveToNext { get; } = new();
        public ReactiveCommand MoveToEnd { get; } = new();
    }
}
