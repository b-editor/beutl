using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data;
using BEditor.Models;
using BEditor.Views;

using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.Timelines
{
    public class TimelineViewModel : BasePropertyChanged
    {
        public bool SeekbarIsMouseDown;
        public bool KeyframeToggle = true;
        public byte ClipLeftRight = 0;
        public bool ClipDrag;
        public PointF ClipStart;
        public ClipData ClipSelect;
        public int MouseLayer;

        public TimelineViewModel(Scene scene)
        {
            Scene = scene;
        }

        public Scene Scene { get; }

        public void TimelineMouseMove(PointF point)
        {
            if (ClipDrag) return;

            if (SeekbarIsMouseDown && KeyframeToggle)
            {
                var s = Scene.ToFrame(point.X);

                Scene.PreviewFrame = s + 1;
            }
        }
        public void TimelineMouseDown(PointF point)
        {
            if (ClipDrag || !KeyframeToggle) return;

            // フラグを"マウス押下中"にする
            SeekbarIsMouseDown = true;

            var s = Scene.ToFrame(point.X);

            Scene.PreviewFrame = s + 1;
        }
        public void TimelineMouseUp()
        {
            SeekbarIsMouseDown = false;

            //保存
            if (ClipLeftRight is not 0 && ClipSelect is not null)
            {

                int start = Scene.ToFrame(ClipSelect.GetCreateClipViewModel().MarginLeft);
                int end = Scene.ToFrame(ClipSelect.GetCreateClipViewModel().Width) + start;//変更後の最大フレーム
                if (0 < start && 0 < end)
                    CommandManager.Do(new ClipData.LengthChangeCommand(ClipSelect, start, end));

                ClipLeftRight = 0;
            }
        }

        public void LayerMouseMove(int layer)
        {
            MouseLayer = layer;
        }

        public void ClipDragStart(PointF point, ClipViewModel clip)
        {
            ClipStart = point;
            ClipSelect = clip.Clip;
            SeekbarIsMouseDown = false;
            ClipDrag = true;
        }
        public void ClipDragEnd()
        {
            ClipSelect = null;
            SeekbarIsMouseDown = false;
            ClipDrag = false;
        }
        public void ClipDrop(PointF point)
        {
            SeekbarIsMouseDown = false;
            ClipDrag = false;

            if (ClipSelect is null) return;

            CommandManager.Do(
                new ClipData.MoveCommand(
                    ClipSelect,
                    Scene.ToFrame(point.X) - Scene.ToFrame(ClipStart.X),
                    MouseLayer));
        }
    }
}
