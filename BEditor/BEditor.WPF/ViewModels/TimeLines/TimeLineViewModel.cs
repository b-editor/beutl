using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Reactive;
using System.Reactive.Linq;

using BEditor.Models.Extension;
using BEditor.Models.Settings;
using BEditor.Views;
using BEditor.Views.SettingsControl;

using BEditor.Core.Data;
using BEditor.Core.Data.Control;
using BEditor.Core.Media;
using BEditor.Models;

using CommandManager = BEditor.Core.Command.CommandManager;
using Reactive.Bindings;

namespace BEditor.ViewModels.TimeLines
{
    public sealed class TimeLineViewModel : BasePropertyChanged
    {
        #region 
        public bool ViewLoaded { get; set; }
        public Scene Scene { get; set; }
        /// <summary>
        /// シークバー移動 マウス押下中フラグ
        /// </summary>
        public bool SeekbarIsMouseDown { get; set; }

        /// <summary>
        /// ラベル移動 マウス押下中フラグ
        /// </summary>
        public bool ClipMouseDown { get; set; }

        /// <summary>
        /// オブジェクト移動開始時のPoint
        /// </summary>
        public Point ClipStart { get; set; }
        /// <summary>
        /// 長さ変更
        /// </summary>
        public byte ClipLeftRight { get; set; } = 0;
        /// <summary>
        /// 時間が未変更の場合True
        /// </summary>
        public bool ClipTimeChange { get; set; }
        /// <summary>
        /// 選択オブジェクトのラベル
        /// </summary>
        public ClipData ClipSelect { get; set; }
        /// <summary>
        /// 移動量
        /// </summary>
        public double ClipMovement { get; set; }
        public int Select_Layer { get; set; }
        public int Select_Frame { get; set; }
        public int Mouse_Layer { get; set; }
        #endregion


        /// <summary>
        /// キーフレーム マウス押下中フラグ 移動中にfalse
        /// </summary>
        public bool KeyframeToggle = true;

        #region Properties

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
        public Action<float, int, int> ResetScale { get; set; }
        public Action<ClipData, int> ClipLayerMoveCommand { get; set; }
        public Func<Point2> GetLayerMousePosition { get; set; }

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

                        ResetScale?.Invoke(Scene.TimeLineZoom, Scene.TotalFrame, AppData.Current.Project.Framerate);
                    }

                });

            scene.ObserveProperty(s => s.PreviewFrame)
                .Subscribe(_ =>
                {
                    SeekbarMargin.Value = new Thickness(ToPixel(Scene.PreviewFrame), 0, 0, 0);

                    AppData.Current.Project.PreviewUpdate();
                });

            scene.ObserveProperty(s => s.TotalFrame)
                .Subscribe(_ =>
                {
                    TrackWidth.Value = ToPixel(Scene.TotalFrame);

                    //目盛り追加
                    ResetScale?.Invoke(Scene.TimeLineZoom, Scene.TotalFrame, AppData.Current.Project.Framerate);
                });

            #region Commandの購読

            SettingShowCommand.Subscribe(SettingWindow);
            PasteCommand.Subscribe(PasteClick);

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

            #region ショートカット

            //CopyCommand = new(() => {
            //    if (Scene.SelectName != null) {
            //        UndoRedoManager.Do(new CopyClip(Scene.Get(Scene.SelectName)));
            //    }
            //});
            //CutCommand = new(() => {
            //    if (Scene.SelectName != null) {
            //        UndoRedoManager.Do(new CutClip(Scene.Get(Scene.SelectName)));
            //    }
            //});
            //DeleteCommand = new(() => {
            //    if (Scene.SelectName != null) {
            //        UndoRedoManager.Do(new RemoveClip(Scene.SelectItem));
            //    }
            //});

            #endregion
        }


        #region ContextMenuCommand

        public ReactiveCommand SettingShowCommand { get; } = new();
        public ReactiveCommand PasteCommand { get; } = new();


        #region SettingWindow
        public void SettingWindow() => new SettingsWindow().ShowDialog();
        #endregion

        #region Paste
        public void PasteClick()
        {
            //UndoRedoManager.Do(new PasteClip(Scene, Select_Frame, Select_Layer));
        }
        #endregion

        #endregion


        #region Scroll同期
        public ReactiveCommand ScrollLineCommand { get; } = new();
        public ReactiveCommand ScrollLabelCommand { get; } = new();
        #endregion

        #region レイヤー操作

        public ReactiveCommand<object> LayerSelectCommand { get; } = new();
        public ReactiveCommand<(object sender, EventArgs args)> LayerDropCommand { get; } = new();
        public ReactiveCommand<(object sender, EventArgs args)> LayerDragOverCommand { get; } = new();
        public ReactiveCommand<object> LayerMoveCommand { get; } = new();


        #region Select
        private void LayerSelect(object sender)
        {
            var grid = (Grid)sender;
            Select_Layer = AttachmentProperty.GetInt(grid);
            Select_Frame = ToFrame(Mouse.GetPosition(grid).X);
        }
        #endregion

        #region Drop
        private static ObjectMetadata FileTypeConvert(string file)
        {
            var ex = Path.GetExtension(file);
            if (ex is ".avi" or ".mp4")
            {
                return ClipType.VideoMetadata;
            }
            else if (ex is ".jpg" or ".jpeg" or ".png" or ".bmp")
            {
                return ClipType.ImageMetadata;
            }
            else if (ex is ".txt")
            {
                return ClipType.TextMetadata;
            }

            return ClipType.FigureMetadata;
        }

        private void LayerDrop(object sender, EventArgs e)
        {
            if (e is DragEventArgs de)
            {
                de.Effects = DragDropEffects.Copy; // マウスカーソルをコピーにする。

                int frame = ToFrame(de.GetPosition(Scene.GetCreateTimeLineView().Layer).X);


                int addlayer = AttachmentProperty.GetInt((Grid)sender);


                if (de.Data.GetDataPresent(typeof(Func<ObjectMetadata>)))
                {
                    var command = new ClipData.AddCommand(
                        Scene,
                        frame,
                        addlayer,
                        ((Func<ObjectMetadata>)de.Data.GetData(typeof(Func<ObjectMetadata>))).Invoke());
                    CommandManager.Do(command);
                }
                else if (de.Data.GetDataPresent(DataFormats.FileDrop, true))
                {
                    string file = (de.Data.GetData(DataFormats.FileDrop) as string[])[0];

                    if (Path.GetExtension(file) != ".beo")
                    {
                        var type_ = FileTypeConvert(file);
                        var a = new ClipData.AddCommand(Scene, frame, addlayer, type_);
                        CommandManager.Do(a);

                        if (type_ == ClipType.ImageMetadata)
                        {
                            (a.data.Effect[0] as Core.Data.Primitive.Objects.PrimitiveImages.Image).File.File = file;
                        }
                        else if (type_ == ClipType.VideoMetadata)
                        {
                            (a.data.Effect[0] as Core.Data.Primitive.Objects.PrimitiveImages.Video).File.File = file;
                        }
                        else if (type_ == ClipType.TextMetadata)
                        {
                            var reader = new StreamReader(file);
                            (a.data.Effect[0] as Core.Data.Primitive.Objects.PrimitiveImages.Text).Document.Text = reader.ReadToEnd();
                            reader.Close();
                        }

                        AppData.Current.Project.PreviewUpdate(a.data);
                    }
                }
            }
        }
        #endregion

        #region DragOver
        private void LayerDragOver(object sender, EventArgs e)
        {
            if (e is DragEventArgs de)
            {
                de.Effects = DragDropEffects.Copy;

                de.Handled = true;
            }
        }
        #endregion

        #region MouseMove
        private void LayerMouseMove(object sender) => Mouse_Layer = AttachmentProperty.GetInt((Grid)sender);
        #endregion

        #endregion

        #region ショートカット

        public ReactiveCommand CopyCommand { get; } = new();

        public ReactiveCommand CutCommand { get; } = new();

        public ReactiveCommand DeleteCommand { get; } = new();

        #endregion


        #region タイムライン操作

        public ReactiveCommand<Point> TimeLineMouseLeftDownCommand { get; } = new();
        public ReactiveCommand TimeLineMouseLeftUpCommand { get; } = new();
        public ReactiveCommand<Point> TimeLineMouseMoveCommand { get; } = new();
        public ReactiveCommand TimeLineMouseLeaveCommand { get; } = new();



        #region Loaded
        public void TimeLineLoaded(Action<ObservableCollection<ClipData>> action)
        {
            var from = Scene.TimeLineZoom;
            Scene.TimeLineZoom = 50;
            Scene.TimeLineZoom = from; //遠回しにviewを更新する

            TrackWidth.Value = ToPixel(Scene.TotalFrame);

            //目盛り追加
            ResetScale?.Invoke(Scene.TimeLineZoom, Scene.TotalFrame, AppData.Current.Project.Framerate);

            action?.Invoke(Scene.Datas);

            SeekbarMargin.Value = new Thickness(ToPixel(Scene.PreviewFrame), 0, 0, 0);
        }
        #endregion

        #region MouseLeftDown
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
        #endregion

        #region MouseLeftUp
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
                    CommandManager.Do(new ClipData.LengthChangeCommand(ClipSelect, start, end));

                ClipLeftRight = 0;
            }
        }
        #endregion

        #region MouseMove
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
                var selectviewmodel = ClipSelect.GetCreateClipViewModel();
                if (selectviewmodel.ClipCursor.Value == Cursors.Arrow && LayerCursor.Value == Cursors.Arrow)
                {
                    #region 横の移動

                    obj_Now = point; //現在のマウス

                    var column = ToFrame(obj_Now.X) - ToFrame(ClipStart.X) + ToFrame(selectviewmodel.MarginProperty.Value.Left);

                    if (column < 0)
                    {
                        selectviewmodel.MarginProperty.Value = new Thickness(0, 1, 0, 0);
                    }
                    else
                    {
                        selectviewmodel.MarginProperty.Value = new Thickness(ToPixel(column), 1, 0, 0);
                    }

                    #endregion


                    #region 縦の移動

                    int tolayer = Mouse_Layer;

                    if (selectviewmodel.Row != tolayer && tolayer != 0)
                    {
                        ClipLayerMoveCommand?.Invoke(ClipSelect, tolayer);
                    }

                    #endregion

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
        #endregion

        #region MouseLeave
        public void TimeLineMouseLeave()
        {
            SeekbarIsMouseDown = false;

            ClipMouseDown = false;
            LayerCursor.Value = Cursors.Arrow;


            if (ClipTimeChange)
            {
                ClipData data = ClipSelect;

                CommandManager.Do(new ClipData.MoveCommand(clip: data,
                                                     toFrame: ToFrame(data.GetCreateClipViewModel().MarginLeftProperty),
                                                     toLayer: ClipSelect.GetCreateClipViewModel().Row));

                ClipTimeChange = false;
            }
        }
        #endregion

        #endregion



        #region フレーム番号を座標に変換
        public double ToPixel(int number) => Setting.WidthOf1Frame * (Scene.TimeLineZoom / 200) * number;
        #endregion

        #region 座標をフレーム番号に変換
        public int ToFrame(double pixel) => (int)(pixel / (Setting.WidthOf1Frame * (Scene.TimeLineZoom / 200)));
        #endregion
    }
}
