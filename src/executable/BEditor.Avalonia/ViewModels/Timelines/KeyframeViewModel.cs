using System;
using System.Reactive.Disposables;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Media;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.Timelines
{
    public sealed class KeyframeViewModel : IKeyframePropertyViewModel
    {
        private readonly CompositeDisposable _disposable = new();

        public KeyframeViewModel(IKeyframeProperty property)
        {
            Property = property;

            AddKeyFrameCommand
                .Subscribe(x => Property.AddFrame(x).Execute())
                .AddTo(_disposable);

            RemoveKeyFrameCommand
                .Subscribe(x => Property.RemoveFrame(x).Execute())
                .AddTo(_disposable);

            MoveKeyFrameCommand
                .Subscribe(x => Property.MoveFrame(x.Item1, x.Item2).Execute())
                .AddTo(_disposable);

            property.Added += Property_Added;
            property.Removed += Property_Removed;
            property.Moved += Property_Moved;
        }

        ~KeyframeViewModel()
        {
            Dispose();
        }

        public Action<float, int>? AddKeyFrameIcon { get; set; }

        public Action<int>? RemoveKeyFrameIcon { get; set; }

        public Action<int, int>? MoveKeyFrameIcon { get; set; }

        public IKeyframeProperty Property { get; }

        public ReactiveCommand<float> AddKeyFrameCommand { get; } = new();

        public ReactiveCommand<float> RemoveKeyFrameCommand { get; } = new();

        public ReactiveCommand<(int, float)> MoveKeyFrameCommand { get; } = new();

        private void Property_Added(float arg1, int arg2)
        {
            AddKeyFrameIcon?.Invoke(arg1, arg2);
        }

        private void Property_Removed(int obj)
        {
            RemoveKeyFrameIcon?.Invoke(obj);
        }

        private void Property_Moved(int arg1, int arg2)
        {
            MoveKeyFrameIcon?.Invoke(arg1, arg2);
        }

        public void Dispose()
        {
            _disposable.Dispose();
            Property.Added -= Property_Added;
            Property.Removed -= Property_Removed;
            Property.Moved -= Property_Moved;
            AddKeyFrameIcon = null;
            RemoveKeyFrameIcon = null;
            MoveKeyFrameIcon = null;

            GC.SuppressFinalize(this);
        }
    }
}