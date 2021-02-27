using System;
using System.Reactive.Disposables;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Media;
using BEditor.Models;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.TimeLines
{
    public sealed class ColorAnimationViewModel : IDisposable
    {
        private readonly CompositeDisposable _disposable = new();

        public ColorAnimationViewModel(ColorAnimationProperty colorProperty)
        {
            Property = colorProperty;
            Metadata = colorProperty.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactiveProperty()
                .AddTo(_disposable);

            AddKeyFrameCommand
                .Subscribe(x => Property.AddFrame(x).Execute())
                .AddTo(_disposable);

            RemoveKeyFrameCommand
                .Subscribe(x => Property.RemoveFrame(x).Execute())
                .AddTo(_disposable);

            MoveKeyFrameCommand
                .Subscribe(x => Property.MoveFrame(x.Item1, x.Item2).Execute())
                .AddTo(_disposable);

            colorProperty.AddKeyFrameEvent
                .Subscribe(value => AddKeyFrameIcon?.Invoke(value.frame, value.index))
                .AddTo(_disposable);

            colorProperty.RemoveKeyFrameEvent
                .Subscribe(value => RemoveKeyFrameIcon?.Invoke(value))
                .AddTo(_disposable);

            colorProperty.MoveKeyFrameEvent
                .Subscribe(value => MoveKeyFrameIcon?.Invoke(value.fromindex, value.toindex))
                .AddTo(_disposable);
        }
        ~ColorAnimationViewModel()
        {
            Dispose();
        }


        public Action<int, int>? AddKeyFrameIcon { get; set; }
        public Action<int>? RemoveKeyFrameIcon { get; set; }
        public Action<int, int>? MoveKeyFrameIcon { get; set; }

        public double TrackHeight => Setting.ClipHeight + 1;
        public ColorAnimationProperty Property { get; }

        public ReadOnlyReactiveProperty<ColorAnimationPropertyMetadata?> Metadata { get; }
        public ReactiveCommand<Frame> AddKeyFrameCommand { get; } = new();
        public ReactiveCommand<Frame> RemoveKeyFrameCommand { get; } = new();
        public ReactiveCommand<(int, int)> MoveKeyFrameCommand { get; } = new();


        public void Dispose()
        {
            _disposable.Dispose();
            _disposable.Clear();
            AddKeyFrameIcon = null;
            RemoveKeyFrameIcon = null;
            MoveKeyFrameIcon = null;

            GC.SuppressFinalize(this);
        }
    }
}
