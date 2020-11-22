using System;

using BEditor.Models.Settings;
using BEditor.ViewModels.Helper;

using BEditor.Core.Data;
using BEditor.Core.Data.Property;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Command;

namespace BEditor.ViewModels.TimeLines
{
    public class KeyFrameViewModel
    {
        public double TrackHeight => Setting.ClipHeight;
        public EaseProperty EaseProperty { get; }

        public KeyFrameViewModel(EaseProperty easeProperty)
        {
            EaseProperty = easeProperty;
            AddKeyFrameCommand.Subscribe(x => CommandManager.Do(new EaseProperty.AddCommand(EaseProperty, x)));
            RemoveKeyFrameCommand.Subscribe(x => CommandManager.Do(new EaseProperty.RemoveCommand(EaseProperty, x)));
            MoveKeyFrameCommand.Subscribe(x => CommandManager.Do(new EaseProperty.MoveCommand(EaseProperty, x.Item1, x.Item2)));

            easeProperty.AddKeyFrameEvent += (_, value) => AddKeyFrameIcon?.Invoke(value.frame, value.index);
            easeProperty.DeleteKeyFrameEvent += (_, value) => DeleteKeyFrameIcon?.Invoke(value);
            easeProperty.MoveKeyFrameEvent += (_, value) => MoveKeyFrameIcon?.Invoke(value.fromindex, value.toindex);
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
