using System;

using BEditor.Models.Settings;
using BEditor.ViewModels.Helper;

using BEditor.NET.Data;
using BEditor.NET.Data.PropertyData;

namespace BEditor.ViewModels.TimeLines {
    public class ColorAnimationViewModel {
        public double TrackHeight => Setting.ClipHeight;
        public ColorAnimationProperty ColorAnimationProperty { get; }

        public ColorAnimationViewModel(ColorAnimationProperty colorProperty) {
            ColorAnimationProperty = colorProperty;
            AddKeyFrameCommand.Subscribe(x => UndoRedoManager.Do(new ColorAnimationProperty.Add(colorProperty, x)));
            RemoveKeyFrameCommand.Subscribe(x => UndoRedoManager.Do(new ColorAnimationProperty.Remove(colorProperty, x)));
            MoveKeyFrameCommand.Subscribe(x => UndoRedoManager.Do(new ColorAnimationProperty.Move(colorProperty, x.Item1, x.Item2)));

            colorProperty.AddKeyFrameEvent += (_, value) => AddKeyFrameIcon?.Invoke(value.frame, value.index);
            colorProperty.DeleteKeyFrameEvent += (_, value) => DeleteKeyFrameIcon?.Invoke(value);
            colorProperty.MoveKeyFrameEvent += (_, value) => MoveKeyFrameIcon?.Invoke(value.fromindex, value.toindex);
        }

        #region View操作のAction

        public Action<int, int> AddKeyFrameIcon { get; set; }
        public Action<int> DeleteKeyFrameIcon { get; set; }
        public Action<int, int> MoveKeyFrameIcon { get; set; }

        #endregion

        public DelegateCommand<int> AddKeyFrameCommand { get; } = new DelegateCommand<int>();
        public DelegateCommand<int> RemoveKeyFrameCommand { get; } = new DelegateCommand<int>();
        public DelegateCommand<(int, int)> MoveKeyFrameCommand { get; } = new DelegateCommand<(int, int)>();
    }
}
