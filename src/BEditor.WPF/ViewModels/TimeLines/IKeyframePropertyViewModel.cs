using System;

using BEditor.Data.Property;
using BEditor.Media;

using Reactive.Bindings;

namespace BEditor.ViewModels.TimeLines
{
    public interface IKeyframePropertyViewModel : IDisposable
    {
        public IKeyFrameProperty Property { get; }
        public Action<int, int>? AddKeyFrameIcon { get; set; }
        public Action<int>? RemoveKeyFrameIcon { get; set; }
        public Action<int, int>? MoveKeyFrameIcon { get; set; }
        public ReactiveCommand<Frame> AddKeyFrameCommand { get; }
        public ReactiveCommand<Frame> RemoveKeyFrameCommand { get; }
        public ReactiveCommand<(int, int)> MoveKeyFrameCommand { get; }
    }
}