using System;
using System.ComponentModel;
using System.IO;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using BEditor;
using BEditor.Command;
using BEditor.Data;
using BEditor.Models;
using BEditor.Models.Extension;
using BEditor.Properties;
using BEditor.Views;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.TimeLines
{
    public sealed class ClipUIViewModel : IDisposable
    {
        private Point MouseRightPoint;
        private readonly CompositeDisposable _disposable = new();


        public ClipUIViewModel(ClipElement clip)
        {
            static CustomClipUIAttribute GetAtt(ObjectElement self)
            {
                var type = self.GetType();
                var attribute = Attribute.GetCustomAttribute(type, typeof(CustomClipUIAttribute));

                if (attribute is CustomClipUIAttribute uIAttribute) return uIAttribute;
                else return new();
            }

            ClipElement = clip;
            WidthProperty.Value = TimeLineViewModel.ToPixel(ClipElement.Length);
            MarginProperty.Value = new Thickness(TimeLineViewModel.ToPixel(ClipElement.Start), 1, 0, 0);
            Row = clip.Layer;

            if (clip.Effect[0] is ObjectElement @object)
            {
                var color = GetAtt(@object).GetColor;
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

            ClipMouseLeftDownCommand.Subscribe(ClipMouseLeftDown).AddTo(_disposable);
            ClipMouseRightDownCommand.Subscribe(p => MouseRightPoint = p).AddTo(_disposable);
            ClipMouseUpCommand.Subscribe(ClipMouseUp).AddTo(_disposable);
            ClipMouseMoveCommand.Subscribe(ClipMouseMove).AddTo(_disposable);
            ClipMouseDoubleClickCommand.Subscribe(ClipMouseDoubleClick).AddTo(_disposable);

            ClipCopyCommand.Subscribe(ClipCopy).AddTo(_disposable);
            ClipCutCommand.Subscribe(ClipCut).AddTo(_disposable);
            ClipDeleteCommand.Subscribe(ClipDelete).AddTo(_disposable);
            ClipDataLogCommand.Subscribe(ClipDataLog).AddTo(_disposable);
            ClipSeparateCommand.Subscribe(ClipSeparate).AddTo(_disposable);

            #endregion

            ClipElement.PropertyChangedAsObservable()
                .Where(e => e.PropertyName is nameof(ClipElement.End))
                .Subscribe(_ => WidthProperty.Value = TimeLineViewModel.ToPixel(ClipElement.Length))
                .AddTo(_disposable);
            
            ClipElement.PropertyChangedAsObservable()
                .Where(e => e.PropertyName is nameof(ClipElement.Start))
                .Subscribe(_ =>
                {
                    MarginLeftProperty = TimeLineViewModel.ToPixel(ClipElement.Start);
                    WidthProperty.Value = TimeLineViewModel.ToPixel(ClipElement.Length);
                })
                .AddTo(_disposable);

            ClipElement.PropertyChangedAsObservable()
                .Where(e=>e.PropertyName is nameof(ClipElement.Layer))
                .Subscribe(_ => TimeLineViewModel.ClipLayerMoveCommand?.Invoke(ClipElement, ClipElement.Layer))
                .AddTo(_disposable);
        }
        ~ClipUIViewModel()
        {
            Dispose();
        }


        #region Properties
        public Scene Scene => ClipElement.Parent;

        private TimeLineViewModel TimeLineViewModel => Scene.GetCreateTimeLineViewModel();

        public ClipElement ClipElement { get; }

        public int Row { get; set; }

        public ReactivePropertySlim<string> ClipText { get; set; } = new();

        public ReactivePropertySlim<Brush> ClipColor { get; set; } = new();

        public static double TrackHeight => Setting.ClipHeight;

        public ReactivePropertySlim<double> WidthProperty { get; } = new();

        public ReactivePropertySlim<Thickness> MarginProperty { get; } = new();

        public double MarginLeftProperty
        {
            get => MarginProperty.Value.Left;
            set
            {
                var tmp = MarginProperty.Value;
                MarginProperty.Value = new(value, tmp.Top, tmp.Right, tmp.Bottom);
            }
        }

        public ReactivePropertySlim<bool> IsExpanded { get; } = new();

        public ReactivePropertySlim<Cursor> ClipCursor { get; } = new();

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


        private void ClipMouseLeftDown()
        {
            TimeLineViewModel.ClipMouseDown = true;

            TimeLineViewModel.ClipStart = TimeLineViewModel.GetLayerMousePosition?.Invoke() ?? TimeLineViewModel.ClipStart;


            TimeLineViewModel.ClipSelect = ClipElement;


            if (TimeLineViewModel.ClipSelect.GetCreateClipViewModel().ClipCursor.Value == Cursors.SizeWE)
            {
                TimeLineViewModel.LayerCursor.Value = Cursors.SizeWE;
            }
        }

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
                ClipElement data = TimeLineViewModel.ClipSelect;

                int start = TimeLineViewModel.ToFrame(TimeLineViewModel.ClipSelect.GetCreateClipViewModel().MarginLeftProperty);
                int end = TimeLineViewModel.ToFrame(TimeLineViewModel.ClipSelect.GetCreateClipViewModel().WidthProperty.Value) + start;//変更後の最大フレーム

                if (0 < start && 0 < end)
                    data.ChangeLength(start, end).Execute();
            }

            if (TimeLineViewModel.ClipTimeChange)
            {
                ClipElement data = TimeLineViewModel.ClipSelect;

                int frame = TimeLineViewModel.ToFrame(data.GetCreateClipViewModel().MarginLeftProperty);
                int layer = data.GetCreateClipViewModel().Row;


                data.MoveFrameLayer(frame, layer).Execute();

                TimeLineViewModel.ClipTimeChange = false;
            }

            //存在しない場合
            Scene.SetCurrentClip(ClipElement);

            TimeLineViewModel.ClipLeftRight = 0;
            TimeLineViewModel.LayerCursor.Value = Cursors.Arrow;
        }

        private void ClipMouseMove(Point point)
        {
            double horizon = point.X;

            if (horizon < 10)
            {
                ClipCursor.Value = Cursors.SizeWE;//右側なら左右矢印↔
                TimeLineViewModel.ClipLeftRight = 1;
            }
            else if (horizon > TimeLineViewModel.ToPixel(ClipElement.Length) - 10)
            {
                ClipCursor.Value = Cursors.SizeWE;
                TimeLineViewModel.ClipLeftRight = 2;
            }
            else
            {
                ClipCursor.Value = Cursors.Arrow;
            }
        }

        private void ClipMouseDoubleClick()
        {
            IsExpanded.Value = !IsExpanded.Value;
        }

        private async void ClipCopy()
        {
            await using var memory = new MemoryStream();
            await Serialize.SaveToStreamAsync(ClipElement, memory, SerializeMode.Json);

            var json = Encoding.Default.GetString(memory.ToArray());
            Clipboard.SetText(json); ;
        }

        private async void ClipCut()
        {
            ClipElement.Parent.RemoveClip(ClipElement).Execute();

            await using var memory = new MemoryStream();
            await Serialize.SaveToStreamAsync(ClipElement, memory, SerializeMode.Json);

            var json = Encoding.Default.GetString(memory.ToArray());
            Clipboard.SetText(json);
        }

        private void ClipDelete()
        {
            ClipElement.Parent.RemoveClip(ClipElement).Execute();
        }

        private void ClipDataLog()
        {
            string text =
                $"ID : {ClipElement.Id}\n" +
                $"Name : {ClipElement.Name}\n" +
                $"Length : {ClipElement.Length.Value}\n" +
                $"Layer : {ClipElement.Layer}\n" +
                $"Start : {ClipElement.Start.Value}\n" +
                $"End : {ClipElement.End.Value}";

            ClipElement.ServiceProvider?.GetRequiredService<IMessage>().DialogAsync(text).ConfigureAwait(false);
        }

        private void ClipSeparate()
        {
            var frame = TimeLineViewModel.ToFrame(MouseRightPoint.X) + ClipElement.Start;

            ClipElement.Split(frame).Execute();
        }

        public void Dispose()
        {
            _disposable.Dispose();
            _disposable.Clear();

            GC.SuppressFinalize(this);
        }
    }
}
