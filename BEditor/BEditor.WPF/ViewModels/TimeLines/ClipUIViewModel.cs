using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using BEditor.Models.Extension;
using BEditor.Models.Settings;
using BEditor.ViewModels.Helper;
using BEditor.Views;

using BEditor.Core.Data;
using BEditor.Core.Data.ObjectData;
using BEditor.Core.Data.ProjectData;
using BEditor.Core.Properties;

namespace BEditor.ViewModels.TimeLines
{
    public class ClipUIViewModel
    {
        public Scene Scene => ClipData.Parent;
        private TimeLineViewModel TimeLineViewModel => Scene.GetCreateTimeLineViewModel();
        public ClipData ClipData { get; }
        /// <summary>
        /// GUIのレイヤー
        /// </summary>
        public int Row { get; set; }


        #region Binding用プロパティ
        public DelegateProperty<string> ClipText { get; set; } = new DelegateProperty<string>();
        public DelegateProperty<Brush> ClipColor { get; set; } = new DelegateProperty<Brush>();
        public double TrackHeight => Setting.ClipHeight;
        public DelegateProperty<double> WidthProperty { get; } = new DelegateProperty<double>();
        public DelegateProperty<Thickness> MarginProperty { get; } = new DelegateProperty<Thickness>();

        public double MarginLeftProperty
        {
            get => MarginProperty.Value.Left;
            set
            {
                var tmp = MarginProperty.Value;
                MarginProperty.Value = new Thickness(value, tmp.Top, tmp.Right, tmp.Bottom);
            }
        }

        /// <summary>
        /// 開いている場合True
        /// </summary>
        public DelegateProperty<bool> IsExpanded { get; } = new DelegateProperty<bool>();

        public DelegateProperty<Cursor> ClipCursor { get; } = new DelegateProperty<Cursor>();
        #endregion


        public ClipUIViewModel(ClipData clip)
        {
            ClipData = clip;
            WidthProperty.Value = TimeLineViewModel.ToPixel(ClipData.Length);
            MarginProperty.Value = new Thickness(TimeLineViewModel.ToPixel(ClipData.Start), 1, 0, 0);
            Row = clip.Layer;

            var type = clip.Type;
            if (type == ClipType.Video) { ClipText.Value = Resources.Video; ClipColor.Value = (Brush)App.Current?.FindResource("VideoColor"); }
            else if (type == ClipType.Image) { ClipText.Value = Resources.Image; ClipColor.Value = (Brush)App.Current?.FindResource("ImageColor"); }
            else if (type == ClipType.Text) { ClipText.Value = Resources.Text; ClipColor.Value = (Brush)App.Current?.FindResource("TextColor"); }
            else if (type == ClipType.Figure) { ClipText.Value = Resources.Figure; ClipColor.Value = (Brush)App.Current?.FindResource("ImageColor"); }
            else if (type == ClipType.Camera) { ClipText.Value = Resources.Camera; ClipColor.Value = (Brush)App.Current.FindResource("VideoColor"); }
            else if (type == ClipType.GL3DObject) { ClipText.Value = Resources._3DObject; ClipColor.Value = (Brush)App.Current?.FindResource("VideoColor"); }
            else if (type == ClipType.Scene) { ClipText.Value = Resources.Scenes; ClipColor.Value = (Brush)App.Current?.FindResource("VideoColor"); }


            ClipMouseDownCommand.Subscribe(() => ClipMouseDown());
            ClipMouseUpCommand.Subscribe(() => ClipMouseUp());
            ClipMouseMoveCommand.Subscribe(point => ClipMouseMove(point));
            ClipMouseDoubleClickCommand.Subscribe(() => ClipMouseDoubleClick());

            ClipCopyCommand.Subscribe(() => ClipCopy());
            ClipCutCommand.Subscribe(() => ClipCut());
            ClipDeleteCommand.Subscribe(() => ClipDelete());
            ClipDataLogCommand.Subscribe(() => ClipDataLog());
            ClipSeparateCommand.Subscribe(() => ClipSeparate());

            clip.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ClipData.End))
                {
                    WidthProperty.Value = TimeLineViewModel.ToPixel(clip.Length);
                }
                else if (e.PropertyName == nameof(ClipData.Start))
                {
                    MarginLeftProperty = TimeLineViewModel.ToPixel(ClipData.Start);
                    WidthProperty.Value = TimeLineViewModel.ToPixel(clip.Length);
                }
                else if (e.PropertyName == nameof(ClipData.Layer))
                {
                    TimeLineViewModel.ClipLayerMoveCommand?.Invoke(clip, clip.Layer);
                }
            };
        }


        #region クリップ操作

        #region Properties
        public DelegateCommand ClipMouseDownCommand { get; } = new DelegateCommand();
        public DelegateCommand ClipMouseUpCommand { get; } = new DelegateCommand();
        public DelegateCommand<Point> ClipMouseMoveCommand { get; } = new DelegateCommand<Point>();
        public DelegateCommand ClipMouseDoubleClickCommand { get; } = new DelegateCommand();
        public DelegateCommand ClipCopyCommand { get; } = new DelegateCommand();
        public DelegateCommand ClipCutCommand { get; } = new DelegateCommand();
        public DelegateCommand ClipDeleteCommand { get; } = new DelegateCommand();
        public DelegateCommand ClipDataLogCommand { get; } = new DelegateCommand();
        public DelegateCommand ClipSeparateCommand { get; } = new DelegateCommand();
        #endregion


        #region MouseDown
        private void ClipMouseDown()
        {
            TimeLineViewModel.ClipMouseDown = true;

            TimeLineViewModel.ClipStart = TimeLineViewModel.GetLayerMousePosition?.Invoke().ToWin() ?? TimeLineViewModel.ClipStart;


            TimeLineViewModel.ClipSelect = ClipData;


            if (TimeLineViewModel.ClipSelect.GetCreateClipViewModel().ClipCursor.Value == Cursors.SizeWE)
            {
                TimeLineViewModel.LayerCursor.Value = Cursors.SizeWE;
            }
        }
        #endregion

        #region MouseUp
        private void ClipMouseUp()
        {
            TimeLineViewModel.SeekbarIsMouseDown = false;

            if (TimeLineViewModel.ClipSelect == null)
            {
                return;
            }

            TimeLineViewModel.ClipMouseDown = false;

            //保存
            if (TimeLineViewModel.ClipLeftRight != 0)
            {
                ClipData data = TimeLineViewModel.ClipSelect;

                int start = TimeLineViewModel.ToFrame(TimeLineViewModel.ClipSelect.GetCreateClipViewModel().MarginLeftProperty);
                int end = TimeLineViewModel.ToFrame(TimeLineViewModel.ClipSelect.GetCreateClipViewModel().WidthProperty.Value) + start;//変更後の最大フレーム

                if (0 < start && 0 < end)
                    UndoRedoManager.Do(new ClipData.LengthChangeCommand(data, start, end));
            }

            if (TimeLineViewModel.ClipTimeChange)
            {
                ClipData data = TimeLineViewModel.ClipSelect;

                int frame = TimeLineViewModel.ToFrame(data.GetCreateClipViewModel().MarginLeftProperty);
                int layer = data.GetCreateClipViewModel().Row;


                UndoRedoManager.Do(new ClipData.MoveCommand(data, frame, layer));

                TimeLineViewModel.ClipTimeChange = false;
            }

            //存在しない場合
            Scene.SetCurrentClip(ClipData);

            TimeLineViewModel.ClipLeftRight = 0;
            TimeLineViewModel.LayerCursor.Value = Cursors.Arrow;
        }
        #endregion

        #region MouseMove
        private void ClipMouseMove(Point point)
        {

            double horizon = point.X;

            if (horizon < 10)
            {
                ClipCursor.Value = Cursors.SizeWE;//右側なら左右矢印↔
                TimeLineViewModel.ClipLeftRight = 1;
            }
            else if (horizon > TimeLineViewModel.ToPixel(ClipData.Length) - 10)
            {
                ClipCursor.Value = Cursors.SizeWE;
                TimeLineViewModel.ClipLeftRight = 2;
            }
            else
            {
                ClipCursor.Value = Cursors.Arrow;
            }
        }
        #endregion

        #region MouseDoubleClick
        private void ClipMouseDoubleClick() => IsExpanded.Value = !IsExpanded.Value;
        #endregion

        #region Copy
        private void ClipCopy()
        {
            //UndoRedoManager.Do(new CopyClip(ClipData));
        }
        #endregion

        #region Cut
        private void ClipCut()
        {
            //UndoRedoManager.Do(new CutClip(ClipData));
        }
        #endregion

        #region Delete
        private void ClipDelete() => UndoRedoManager.Do(new ClipData.RemoveCommand(ClipData));
        #endregion

        #region DataLog
        private void ClipDataLog()
        {
            string text =
                $"ID : {ClipData.Id}\n" +
                $"Name : {ClipData.Name}\n" +
                $"Length : {ClipData.Length}\n" +
                $"Layer : {ClipData.Layer}\n" +
                $"Type : {ClipData.Type}\n" +
                $"Start : {ClipData.Start}\n" +
                $"End : {ClipData.End}";

            BEditor.Core.Extensions.ViewCommand.Message.Dialog(text);
        }
        #endregion

        #region Sparate
        private void ClipSeparate()
        {

        }
        #endregion

        #endregion
    }
}
