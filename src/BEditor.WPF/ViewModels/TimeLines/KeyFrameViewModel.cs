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

    public sealed class KeyFrameViewModel : IKeyframePropertyViewModel
    {
        private readonly CompositeDisposable _disposable = new();

        public KeyFrameViewModel(IKeyFrameProperty property)
        {
            Property = property;
            Metadata = property.ObserveProperty(p => p.PropertyMetadata)
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

            property.Added += Property_Added;
            property.Removed += Property_Removed;
            property.Moved += Property_Moved;
        }
        ~KeyFrameViewModel()
        {
            Dispose();
        }


        #region View操作のAction

        public Action<int, int>? AddKeyFrameIcon { get; set; }
        public Action<int>? RemoveKeyFrameIcon { get; set; }
        public Action<int, int>? MoveKeyFrameIcon { get; set; }

        #endregion

        public static double TrackHeight => Setting.ClipHeight + 1;
        public IKeyFrameProperty Property { get; }

        public ReadOnlyReactiveProperty<PropertyElementMetadata?> Metadata { get; }
        public ReactiveCommand<Frame> AddKeyFrameCommand { get; } = new();
        public ReactiveCommand<Frame> RemoveKeyFrameCommand { get; } = new();
        public ReactiveCommand<(int, int)> MoveKeyFrameCommand { get; } = new();

        private void Property_Added(Frame arg1, int arg2)
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
            _disposable.Clear();
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
