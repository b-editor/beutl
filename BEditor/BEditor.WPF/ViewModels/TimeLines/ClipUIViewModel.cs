using System;
using System.Reactive;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using BEditor.Models.Extension;
using BEditor.Models.Settings;
using BEditor.Views;

using BEditor.Core.Data;
using BEditor.Core.Properties;

using CommandManager = BEditor.Core.Command.CommandManager;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using System.Reactive.Linq;
using System.ComponentModel;
using BEditor.Core.Command;
using System.Runtime.InteropServices;
using BEditor.Core;
using System.IO;
using System.Text;

namespace BEditor.ViewModels.TimeLines
{
    public class ClipUIViewModel
    {
        private Point MouseRightPoint;

        public Scene Scene => ClipData.Parent;
        private TimeLineViewModel TimeLineViewModel => Scene.GetCreateTimeLineViewModel();
        public ClipData ClipData { get; }
        /// <summary>
        /// GUIのレイヤー
        /// </summary>
        public int Row { get; set; }


        #region Binding用プロパティ
        public ReactiveProperty<string> ClipText { get; set; } = new();
        public ReactiveProperty<Brush> ClipColor { get; set; } = new();
        public double TrackHeight => Setting.ClipHeight;
        public ReactiveProperty<double> WidthProperty { get; } = new();
        public ReactiveProperty<Thickness> MarginProperty { get; } = new();

        public double MarginLeftProperty
        {
            get => MarginProperty.Value.Left;
            set
            {
                var tmp = MarginProperty.Value;
                MarginProperty.Value = new(value, tmp.Top, tmp.Right, tmp.Bottom);
            }
        }

        /// <summary>
        /// 開いている場合True
        /// </summary>
        public ReactiveProperty<bool> IsExpanded { get; } = new();

        public ReactiveProperty<Cursor> ClipCursor { get; } = new();
        #endregion


        public ClipUIViewModel(ClipData clip)
        {
            ClipData = clip;
            WidthProperty.Value = TimeLineViewModel.ToPixel(ClipData.Length);
            MarginProperty.Value = new Thickness(TimeLineViewModel.ToPixel(ClipData.Start), 1, 0, 0);
            Row = clip.Layer;

            if (clip.Effect[0] is ObjectElement @object)
            {
                var color = @object.GetAttribute().GetColor;
                ClipColor.Value = new SolidColorBrush(new Color()
                {
                    R = color.R,
                    G = color.G,
                    B = color.B,
                    A = 255
                });
                ClipText.Value = @object.Name;
            }

            #region Subscribe

            ClipMouseLeftDownCommand.Subscribe(ClipMouseLeftDown);
            ClipMouseRightDownCommand.Subscribe(p => MouseRightPoint = p);
            ClipMouseUpCommand.Subscribe(ClipMouseUp);
            ClipMouseMoveCommand.Subscribe(ClipMouseMove);
            ClipMouseDoubleClickCommand.Subscribe(ClipMouseDoubleClick);

            ClipCopyCommand.Subscribe(ClipCopy);
            ClipCutCommand.Subscribe(ClipCut);
            ClipDeleteCommand.Subscribe(ClipDelete);
            ClipDataLogCommand.Subscribe(ClipDataLog);
            ClipSeparateCommand.Subscribe(ClipSeparate);

            #endregion

            var observable = Observable.FromEvent<PropertyChangedEventHandler, PropertyChangedEventArgs>(
                h => (s, e) => h(e),
                h => clip.PropertyChanged += h,
                h => clip.PropertyChanged -= h);

            observable.Where(e => e.PropertyName is nameof(ClipData.End))
                .Subscribe(_ => WidthProperty.Value = TimeLineViewModel.ToPixel(ClipData.Length));

            observable.Where(e => e.PropertyName is nameof(ClipData.Start))
                .Subscribe(_ =>
                {
                    MarginLeftProperty = TimeLineViewModel.ToPixel(ClipData.Start);
                    WidthProperty.Value = TimeLineViewModel.ToPixel(ClipData.Length);
                });

            observable.Where(e => e.PropertyName is nameof(ClipData.Layer))
                .Subscribe(_ => TimeLineViewModel.ClipLayerMoveCommand?.Invoke(ClipData, ClipData.Layer));
        }


        #region クリップ操作

        #region Properties
        public ReactiveCommand ClipMouseLeftDownCommand { get; } = new();
        public ReactiveCommand<Point> ClipMouseRightDownCommand { get; } = new();
        public ReactiveCommand ClipMouseUpCommand { get; } = new();
        public ReactiveCommand<Point> ClipMouseMoveCommand { get; } = new();
        public ReactiveCommand ClipMouseDoubleClickCommand { get; } = new();
        public ReactiveCommand ClipCopyCommand { get; } = new();
        public ReactiveCommand ClipCutCommand { get; } = new();
        public ReactiveCommand ClipDeleteCommand { get; } = new();
        public ReactiveCommand ClipDataLogCommand { get; } = new();
        public ReactiveCommand ClipSeparateCommand { get; } = new();
        #endregion


        #region MouseLeftDown
        private void ClipMouseLeftDown()
        {
            TimeLineViewModel.ClipMouseDown = true;

            TimeLineViewModel.ClipStart = TimeLineViewModel.GetLayerMousePosition?.Invoke() ?? TimeLineViewModel.ClipStart;


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
                    data.CreateLengthChangeCommand(start, end).Execute();
            }

            if (TimeLineViewModel.ClipTimeChange)
            {
                ClipData data = TimeLineViewModel.ClipSelect;

                int frame = TimeLineViewModel.ToFrame(data.GetCreateClipViewModel().MarginLeftProperty);
                int layer = data.GetCreateClipViewModel().Row;


                data.CreateMoveCommand(frame, layer).Execute();

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
            using var memory = new MemoryStream();
            Serialize.SaveToStream(ClipData, memory, SerializeMode.Json);

            var json = Encoding.Default.GetString(memory.ToArray());
            Clipboard.SetText(json); ;
        }
        #endregion

        #region Cut
        private void ClipCut()
        {
            ClipData.Parent.CreateRemoveCommand(ClipData).Execute();

            using var memory = new MemoryStream();
            Serialize.SaveToStream(ClipData, memory, SerializeMode.Json);

            var json = Encoding.Default.GetString(memory.ToArray());
            Clipboard.SetText(json);
        }
        #endregion

        #region Delete
        private void ClipDelete() => ClipData.Parent.CreateRemoveCommand(ClipData).Execute();
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

            BEditor.Core.Extensions.Message.Dialog(text);
        }
        #endregion

        #region Sparate
        private void ClipSeparate()
        {
            var frame = TimeLineViewModel.ToFrame(MouseRightPoint.X) + ClipData.Start;

            ClipData.CreateSparateCommand(frame).Execute();
        }
        #endregion

        #endregion
    }
}
