using System;
using System.Linq;
using System.Reactive.Disposables;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Media;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.Timelines
{
    public sealed class KeyframeViewModel : IDisposable
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
                .Subscribe(x =>
                {
                    var pos = Property.Enumerate().ElementAt(x.Item1);
                    var length = property.GetRequiredParent<ClipElement>().Length;
                    if (pos.Type == PositionType.Percentage)
                    {
                        Property.MoveFrame(x.Item1, pos.WithValue(x.Item2)).Execute();
                    }
                    else
                    {
                        Property.MoveFrame(x.Item1, pos.WithValue(x.Item2 * length)).Execute();
                    }
                })
                .AddTo(_disposable);

            property.Added += Property_Added;
            property.Removed += Property_Removed;
            property.Moved += Property_Moved;
        }

        ~KeyframeViewModel()
        {
            Dispose();
        }

        public Action<PositionInfo>? AddKeyFrameIcon { get; set; }

        public Action<PositionInfo>? RemoveKeyFrameIcon { get; set; }

        public Action<int, int>? MoveKeyFrameIcon { get; set; }

        public IKeyframeProperty Property { get; }

        public ReactiveCommand<PositionInfo> AddKeyFrameCommand { get; } = new();

        public ReactiveCommand<PositionInfo> RemoveKeyFrameCommand { get; } = new();

        public ReactiveCommand<(int, float)> MoveKeyFrameCommand { get; } = new();

        private void Property_Added(PositionInfo position)
        {
            AddKeyFrameIcon?.Invoke(position);
        }

        private void Property_Removed(PositionInfo position)
        {
            RemoveKeyFrameIcon?.Invoke(position);
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