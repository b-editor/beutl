using System;

using BEditor.Data.Property;
using BEditor.Media;

using Reactive.Bindings;

namespace BEditor.ViewModels.Timelines
{
    public interface IKeyframePropertyViewModel : IDisposable
    {
        public IKeyframeProperty Property { get; }

        public Action<float, int>? AddKeyFrameIcon { get; set; }

        public Action<int>? RemoveKeyFrameIcon { get; set; }

        public Action<int, int>? MoveKeyFrameIcon { get; set; }

        public ReactiveCommand<float> AddKeyFrameCommand { get; }

        public ReactiveCommand<float> RemoveKeyFrameCommand { get; }

        public ReactiveCommand<(int, float)> MoveKeyFrameCommand { get; }
    }
}