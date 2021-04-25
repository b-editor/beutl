using System;

using BEditor.Data.Property;
using BEditor.Media;

using Reactive.Bindings;

namespace BEditor.ViewModels.Timelines
{
    public interface IKeyframePropertyViewModel : IDisposable
    {
        public IKeyframeProperty Property { get; }

        public Action<int, int>? AddKeyFrameIcon { get; set; }

        public Action<int>? RemoveKeyFrameIcon { get; set; }

        public Action<int, int>? MoveKeyFrameIcon { get; set; }

        public ReactiveCommand<Frame> AddKeyFrameCommand { get; }

        public ReactiveCommand<Frame> RemoveKeyFrameCommand { get; }

        public ReactiveCommand<(int, int)> MoveKeyFrameCommand { get; }
    }
}