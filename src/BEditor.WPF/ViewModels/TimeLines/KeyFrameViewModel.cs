using System;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Media;
using BEditor.Models;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.TimeLines
{
    public class KeyFrameViewModel
    {
        public KeyFrameViewModel(EaseProperty easeProperty)
        {
            EaseProperty = easeProperty;
            Metadata = easeProperty.ObserveProperty(p => p.PropertyMetadata)
                .ToReadOnlyReactiveProperty();
            AddKeyFrameCommand.Subscribe(x => EaseProperty.AddFrame(x).Execute());
            RemoveKeyFrameCommand.Subscribe(x => EaseProperty.RemoveFrame(x).Execute());
            MoveKeyFrameCommand.Subscribe(x => EaseProperty.MoveFrame(x.Item1, x.Item2).Execute());

            easeProperty.AddKeyFrameEvent += (_, value) => AddKeyFrameIcon?.Invoke(value.frame, value.index);
            easeProperty.DeleteKeyFrameEvent += (_, value) => DeleteKeyFrameIcon?.Invoke(value);
            easeProperty.MoveKeyFrameEvent += (_, value) => MoveKeyFrameIcon?.Invoke(value.fromindex, value.toindex);
        }

        #region View操作のAction

        public Action<int, int>? AddKeyFrameIcon { get; set; }
        public Action<int>? DeleteKeyFrameIcon { get; set; }
        public Action<int, int>? MoveKeyFrameIcon { get; set; }

        #endregion

        public double TrackHeight => Setting.ClipHeight + 1;
        public EaseProperty EaseProperty { get; }

        public ReadOnlyReactiveProperty<EasePropertyMetadata?> Metadata { get; }
        public ReactiveCommand<Frame> AddKeyFrameCommand { get; } = new();
        public ReactiveCommand<Frame> RemoveKeyFrameCommand { get; } = new();
        public ReactiveCommand<(int, int)> MoveKeyFrameCommand { get; } = new();
    }
}
