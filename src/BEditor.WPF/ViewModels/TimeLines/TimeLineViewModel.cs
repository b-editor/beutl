using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using BEditor.Core.Command;
using BEditor.Core.Data;
using BEditor.Core.Extensions;
using BEditor.Models;
using BEditor.Models.Extension;
using BEditor.Primitive;
using BEditor.Primitive.Objects;
using BEditor.Views;
using BEditor.Views.SettingsControl;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

using Point = System.Windows.Point;

namespace BEditor.ViewModels.TimeLines
{
    public sealed class TimeLineViewModel : BasePropertyChanged
    {
        #region Fields
        public bool ViewLoaded;
        public Scene Scene;
        /// <summary>
        /// シークバー移動 マウス押下中フラグ
        /// </summary>
        public bool SeekbarIsMouseDown;

        /// <summary>
        /// ラベル移動 マウス押下中フラグ
        /// </summary>
        public bool ClipMouseDown;

        /// <summary>
        /// オブジェクト移動開始時のPoint
        /// </summary>
        public Point ClipStart;
        /// <summary>
        /// 長さ変更
        /// </summary>
        public byte ClipLeftRight = 0;
        /// <summary>
        /// 時間が未変更の場合True
        /// </summary>
        public bool ClipTimeChange;
        /// <summary>
        /// 選択オブジェクトのラベル
        /// </summary>
        public ClipElement? ClipSelect;
        /// <summary>
        /// 移動量
        /// </summary>
        public double ClipMovement;
        public int Select_Layer;
        public int Select_Frame;
        public int Mouse_Layer;
        /// <summary>
        /// キーフレーム マウス押下中フラグ 移動中にfalse
        /// </summary>
        public bool KeyframeToggle = true;

        #endregion

        public TimeLineViewModel(Scene scene)
        {
            Scene = scene;

            TrackWidth.Value = ToPixel(Scene.TotalFrame);

            scene.ObserveProperty(s => s.TimeLineZoom)
                .Subscribe(_ =>
                {
                    if (Scene.TimeLineZoom <= 0) Scene.TimeLineZoom = 1;
                    if (Scene.TimeLineZoom >= 201) Scene.TimeLineZoom = 200;

                    if (ViewLoaded)
                    {
                        int l = Scene.TotalFrame;

                        TrackWidth.Value = ToPixel(l);

                        for (int index = 0; index < Scene.Datas.Count; index++)
                        {
                            var info = Scene.Datas[index];
                            double start = ToPixel(info.Start);
                            double length = ToPixel(info.Length);

                            var vm = info.GetCreateClipViewModel();
                            vm.MarginProperty.Value = new Thickness(start, 1, 0, 0);
                            vm.WidthProperty.Value = length;
                        }

                        SeekbarMargin.Value = new Thickness(ToPixel(Scene.PreviewFrame), 0, 0, 0);

                        ResetScale?.Invoke(Scene.TimeLineZoom, Scene.TotalFrame, AppData.Current.Project!.Framerate);
                    }

                });

            scene.ObserveProperty(s => s.PreviewFrame)
                .Subscribe(_ =>
                {
                    SeekbarMargin.Value = new Thickness(ToPixel(Scene.PreviewFrame), 0, 0, 0);

                    var type = RenderType.Preview;

                    if (AppData.Current.AppStatus is Core.Service.Status.Playing) type = RenderType.VideoPreview;

                    AppData.Current.Project!.PreviewUpdate(type);
                });

            scene.ObserveProperty(s => s.TotalFrame)
                .Subscribe(_ =>
                {
                    TrackWidth.Value = ToPixel(Scene.TotalFrame);

                    //目盛り追加
                    ResetScale?.Invoke(Scene.TimeLineZoom, Scene.TotalFrame, AppData.Current.Project!.Framerate);
                });

            #region Commandの購読

            SettingShowCommand.Subscribe(() => new SettingsWindow().ShowDialog());
            AddClip.Subscribe(AddClipCommand);

            ScrollLineCommand.Subscribe(() =>
            {
                var timeline = Scene.GetCreateTimeLineView();
                timeline.ScrollLabel.ScrollToVerticalOffset(timeline.ScrollLine.VerticalOffset);
            });
            ScrollLabelCommand.Subscribe(() =>
            {
                var timeline = Scene.GetCreateTimeLineView();
                timeline.ScrollLine.ScrollToVerticalOffset(timeline.ScrollLabel.VerticalOffset);
            });

            LayerSelectCommand.Subscribe(LayerSelect);
            LayerDropCommand.Subscribe(x => LayerDrop(x.sender, x.args));
            LayerDragOverCommand.Subscribe(x => LayerDragOver(x.sender, x.args));
            LayerMoveCommand.Subscribe(LayerMouseMove);

            TimeLineMouseLeftDownCommand.Subscribe(TimeLineMouseLeftDown);
            TimeLineMouseLeftUpCommand.Subscribe(TimeLineMouseLeftUp);
            TimeLineMouseMoveCommand.Subscribe(TimeLineMouseMove);
            TimeLineMouseLeaveCommand.Subscribe(TimeLineMouseLeave);


            #endregion
        }

        public double TrackHeight { get; } = Setting.ClipHeight;
        public ReactiveProperty<Thickness> SeekbarMargin { get; } = new();
        public ReactiveProperty<double> TrackWidth { get; } = new();
        public ReactiveProperty<Cursor> LayerCursor { get; } = new();

        /// <summary>
        /// 目盛りを追加するAction
        /// <para>
        /// arg1 : zoom
        /// </para>
        /// <para>
        /// arg2 : max
        /// </para>
        /// <para>
        /// arg3 : rate
        /// </para>
        /// </summary>
        public Action<float, int, int>? ResetScale { get; set; }
        public Action<ClipElement, int>? ClipLayerMoveCommand { get; set; }
        public Func<Point>? GetLayerMousePosition { get; set; }


        public ReactiveCommand SettingShowCommand { get; } = new();
        public ReactiveCommand PasteCommand => EditModel.Current.ClipboardPaste;
        public ReactiveCommand<ObjectMetadata> AddClip { get; } = new();

        public ReactiveCommand ScrollLineCommand { get; } = new();
        public ReactiveCommand ScrollLabelCommand { get; } = new();

        public ReactiveCommand<object> LayerSelectCommand { get; } = new();
        public ReactiveCommand<(object sender, EventArgs args)> LayerDropCommand { get; } = new();
        public ReactiveCommand<(object sender, EventArgs args)> LayerDragOverCommand { get; } = new();
        public ReactiveCommand<object> LayerMoveCommand { get; } = new();

        public ReactiveCommand<Point> TimeLineMouseLeftDownCommand { get; } = new();
        public ReactiveCommand TimeLineMouseLeftUpCommand { get; } = new();
        public ReactiveCommand<Point> TimeLineMouseMoveCommand { get; } = new();
        public ReactiveCommand TimeLineMouseLeaveCommand { get; } = new();



        private void AddClipCommand(ObjectMetadata @object)
        {
            if (!Scene.InRange(Select_Frame, Select_Frame + 180, Select_Layer))
            {
                Message.Snackbar("指定した場所にクリップが存在しているため、新しいクリップを配置できません");

                return;
            }

            Scene.AddClip(
                Select_Frame,
                Select_Layer,
                @object,
                out _)
                .Execute();
        }

        private void LayerSelect(object sender)
        {
            var grid = (Grid)sender;
            Select_Layer = AttachmentProperty.GetInt(grid);
            Select_Frame = ToFrame(Mouse.GetPosition(grid).X);
        }
        private void LayerDrop(object sender, EventArgs e)
        {
            if (e is DragEventArgs de)
            {
                de.Effects = DragDropEffects.Copy; // マウスカーソルをコピーにする。

                Media.Frame frame = ToFrame(de.GetPosition(Scene.GetCreateTimeLineView().Layer).X);


                int addlayer = AttachmentProperty.GetInt((Grid)sender);


                if (de.Data.GetDataPresent(typeof(Func<ObjectMetadata>)))
                {
                    var meta = ((Func<ObjectMetadata>)de.Data.GetData(typeof(Func<ObjectMetadata>))).Invoke();

                    var endFrame = frame + new Media.Frame(180);
                    Scene.Clamp(null, ref frame, ref endFrame, addlayer);

                    var recordCommand = Scene.AddClip(frame, addlayer, meta, out var c);

                    c.End = endFrame;

                    recordCommand.Execute();
                }
                else if (de.Data.GetDataPresent(DataFormats.FileDrop, true))
                {
                    string file = (de.Data.GetData(DataFormats.FileDrop) as string[])![0];

                    if (Path.GetExtension(file) != ".beo")
                    {
                        var type_ = EditModel.FileTypeConvert(file);

                        if (!Scene.InRange(frame, frame + 180, addlayer))
                        {
                            Message.Snackbar("指定した場所にクリップが存在しているため、新しいクリップを配置できません");

                            return;
                        }

                        Scene.AddClip(frame, addlayer, type_, out var clip).Execute();

                        if (type_ == PrimitiveTypes.ImageMetadata)
                        {
                            (clip.Effect[0] as ImageFile)!.File.File = file;
                        }
                        else if (type_ == PrimitiveTypes.VideoMetadata)
                        {
                            (clip.Effect[0] as VideoFile)!.File.File = file;
                        }
                        else if (type_ == PrimitiveTypes.TextMetadata)
                        {
                            var reader = new StreamReader(file);
                            (clip.Effect[0] as Text)!.Document.Text = reader.ReadToEnd();
                            reader.Close();
                        }

                        AppData.Current.Project!.PreviewUpdate(clip);
                    }
                }
            }
        }
        private void LayerDragOver(object sender, EventArgs e)
        {
            if (e is DragEventArgs de)
            {
                de.Effects = DragDropEffects.Copy;

                de.Handled = true;
            }
        }
        private void LayerMouseMove(object sender)
        {
            Mouse_Layer = AttachmentProperty.GetInt((Grid)sender);
        }

        public void TimeLineLoaded(Action<ObservableCollection<ClipElement>> action)
        {
            var from = Scene.TimeLineZoom;
            Scene.TimeLineZoom = 50;
            Scene.TimeLineZoom = from; //遠回しにviewを更新する

            TrackWidth.Value = ToPixel(Scene.TotalFrame);

            //目盛り追加
            ResetScale?.Invoke(Scene.TimeLineZoom, Scene.TotalFrame, AppData.Current.Project!.Framerate);

            action?.Invoke(Scene.Datas);

            SeekbarMargin.Value = new Thickness(ToPixel(Scene.PreviewFrame), 0, 0, 0);
        }
        public void TimeLineMouseLeftDown(Point point)
        {
            if (ClipMouseDown || !KeyframeToggle)
            {
                return;
            }

            // フラグを"マウス押下中"にする
            SeekbarIsMouseDown = true;

            var s = ToFrame(point.X);

            Scene.PreviewFrame = s + 1;
        }
        public void TimeLineMouseLeftUp()
        {
            // マウス押下中フラグを落とす
            SeekbarIsMouseDown = false;
            LayerCursor.Value = Cursors.Arrow;

            //保存
            if (ClipLeftRight != 0 && ClipSelect != null)
            {

                int start = ToFrame(ClipSelect.GetCreateClipViewModel().MarginLeftProperty);
                int end = ToFrame(ClipSelect.GetCreateClipViewModel().WidthProperty.Value) + start;//変更後の最大フレーム
                if (0 < start && 0 < end)
                    ClipSelect.ChangeLength(start, end).Execute();

                ClipLeftRight = 0;
            }
        }
        public void TimeLineMouseMove(Point point)
        {
            //マウスの現在フレーム
            Point obj_Now;

            if (SeekbarIsMouseDown && KeyframeToggle)
            {
                var s = ToFrame(point.X);

                Scene.PreviewFrame = s + 1;
            }
            else if (ClipMouseDown)
            {
                if (ClipSelect is null) return;
                var selectviewmodel = ClipSelect.GetCreateClipViewModel();
                if (selectviewmodel.ClipCursor.Value == Cursors.Arrow && LayerCursor.Value == Cursors.Arrow)
                {
                    obj_Now = point; //現在のマウス

                    var newframe = ToFrame(obj_Now.X) - ToFrame(ClipStart.X) + ToFrame(selectviewmodel.MarginProperty.Value.Left);
                    int newlayer = Mouse_Layer;

                    if (!Scene.InRange(ClipSelect, newframe, newframe + ClipSelect.Length, newlayer))
                    {
                        return;
                    }

                    // 横の移動
                    if (newframe < 0)
                    {
                        selectviewmodel.MarginProperty.Value = new Thickness(0, 1, 0, 0);
                    }
                    else
                    {
                        selectviewmodel.MarginProperty.Value = new Thickness(ToPixel(newframe), 1, 0, 0);
                    }

                    // 縦の移動
                    if (selectviewmodel.Row != newlayer && newlayer != 0)
                    {
                        ClipLayerMoveCommand?.Invoke(ClipSelect, newlayer);
                    }


                    ClipStart = obj_Now;

                    ClipTimeChange = true;
                }
                else
                {
                    obj_Now = point; //現在のマウスの位置


                    ClipMovement = ToPixel(ToFrame(obj_Now.X) - ToFrame(ClipStart.X)); //一時的な移動量

                    if (ClipLeftRight == 2)
                    { //左
                        selectviewmodel.WidthProperty.Value += ClipMovement;
                    }
                    else if (ClipLeftRight == 1)
                    {
                        var a = ClipMovement;
                        selectviewmodel.WidthProperty.Value -= a;
                        selectviewmodel.MarginProperty.Value = new Thickness(selectviewmodel.MarginProperty.Value.Left + a, 1, 0, 0);
                    }

                    ClipStart = obj_Now;
                }
            }
        }
        public void TimeLineMouseLeave()
        {
            SeekbarIsMouseDown = false;

            ClipMouseDown = false;
            LayerCursor.Value = Cursors.Arrow;


            if (ClipTimeChange && ClipSelect is not null)
            {
                ClipElement data = ClipSelect;

                var toframe = ToFrame(data.GetCreateClipViewModel().MarginLeftProperty);

                if (Media.Frame.Zero > toframe)
                {
                    data.MoveFrameLayer(
                        toframe,
                        ClipSelect.GetCreateClipViewModel().Row).Execute();
                }

                ClipTimeChange = false;
            }
        }



        /// <summary>
        /// フレーム番号を座標に変換
        /// </summary>
        /// <param name="number"></param>
        /// <returns></returns>
        public double ToPixel(int number)
        {
            return Setting.WidthOf1Frame * (Scene.TimeLineZoom / 200) * number;
        }
        /// <summary>
        /// 座標をフレーム番号に変換
        /// </summary>
        /// <param name="pixel"></param>
        /// <returns></returns>
        public int ToFrame(double pixel)
        {
            return (int)(pixel / (Setting.WidthOf1Frame * (Scene.TimeLineZoom / 200)));
        }
    }
}
