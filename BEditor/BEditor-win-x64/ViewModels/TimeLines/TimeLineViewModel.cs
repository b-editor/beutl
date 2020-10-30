using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using BEditor.Models.Extension;
using BEditor.Models.Settings;
using BEditor.ViewModels.Helper;
using BEditor.Views;
using BEditor.Views.SettingsControl;
using BEditor.Views.TimeLines;

using BEditor.NET.Data;
using BEditor.NET.Data.ObjectData;
using BEditor.NET.Data.ProjectData;
using BEditor.NET.Interfaces;
using BEditor.NET.Media;

namespace BEditor.ViewModels.TimeLines {
    public class TimeLineViewModel : BasePropertyChanged {
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
        public DelegateProperty<Thickness> SeekbarMargin { get; } = new();
        public DelegateProperty<double> TrackWidth { get; } = new();
        public DelegateProperty<Cursor> LayerCursor { get; } = new();

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


        public TimeLineViewModel(Scene scene) {
            Scene = scene;

            TrackWidth.Value = ToPixel(Scene.TotalFrame);

            Scene.PropertyChanged += (_, e) => {
                if (e.PropertyName == nameof(Scene.TimeLineZoom)) {
                    if (Scene.TimeLineZoom <= 0) {
                        Scene.TimeLineZoom = 1;
                    }

                    if (Scene.TimeLineZoom >= 101) {
                        Scene.TimeLineZoom = 100;
                    }

                    if (ViewLoaded) {
                        int l = Scene.TotalFrame;

                        TrackWidth.Value = ToPixel(l);

                        for (int index = 0; index < Scene.Count; index++) {
                            var info = Scene.Datas[index];
                            double start = ToPixel(info.Start);
                            double length = ToPixel(info.Length);

                            var vm = info.GetCreateClipViewModel();
                            vm.MarginProperty.Value = new Thickness(start, 1, 0, 0);
                            vm.WidthProperty.Value = length;
                        }

                        SeekbarMargin.Value = new Thickness(ToPixel(Scene.PreviewFrame), 0, 0, 0);

                        ResetScale?.Invoke(Scene.TimeLineZoom, Scene.TotalFrame, Component.Current.Project.FrameRate);
                    }
                }
                else if (e.PropertyName == nameof(Scene.PreviewFrame)) {
                    SeekbarMargin.Value = new Thickness(ToPixel(Scene.PreviewFrame), 0, 0, 0);

                    Component.Current.Project.PreviewUpdate();
                }
                else if (e.PropertyName == nameof(Scene.TotalFrame)) {
                    TrackWidth.Value = ToPixel(Scene.TotalFrame);

                    //目盛り追加
                    ResetScale?.Invoke(Scene.TimeLineZoom, Scene.TotalFrame, Component.Current.Project.FrameRate);
                }
            };

            #region Commandの購読

            SettingShowCommand.Subscribe(() => SettingWindow());
            PasteCommand.Subscribe(() => PasteClick());

            ScrollLineCommand.Subscribe(() => {
                var timeline = Scene.GetCreateTimeLineView();
                timeline.ScrollLabel.ScrollToVerticalOffset(timeline.ScrollLine.VerticalOffset);
            });
            ScrollLabelCommand.Subscribe(() => {
                var timeline = Scene.GetCreateTimeLineView();
                timeline.ScrollLine.ScrollToVerticalOffset(timeline.ScrollLabel.VerticalOffset);
            });

            LayerSelectCommand.Subscribe(x => LayerSelect(x));
            LayerDropCommand.Subscribe(x => LayerDrop(x.sender, x.args));
            LayerDragOverCommand.Subscribe(x => LayerDragOver(x.sender, x.args));
            LayerMoveCommand.Subscribe(x => LayerMouseMove(x));

            TimeLineMouseLeftDownCommand.Subscribe(point => TimeLineMouseLeftDown(point));
            TimeLineMouseLeftUpCommand.Subscribe(() => TimeLineMouseLeftUp());
            TimeLineMouseMoveCommand.Subscribe(point => TimeLineMouseMove(point));
            TimeLineMouseLeaveCommand.Subscribe(() => TimeLineMouseLeave());

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

        public DelegateCommand SettingShowCommand { get; } = new DelegateCommand();
        public DelegateCommand PasteCommand { get; } = new DelegateCommand();


        #region SettingWindow
        public void SettingWindow() => new SettingsWindow().ShowDialog();
        #endregion

        #region Paste
        public void PasteClick() {
            //UndoRedoManager.Do(new PasteClip(Scene, Select_Frame, Select_Layer));
        }
        #endregion

        #endregion


        #region Scroll同期
        public DelegateCommand ScrollLineCommand { get; } = new DelegateCommand();
        public DelegateCommand ScrollLabelCommand { get; } = new DelegateCommand();
        #endregion

        #region レイヤー操作

        public DelegateCommand<object> LayerSelectCommand { get; } = new DelegateCommand<object>();
        public DelegateCommand<(object sender, EventArgs args)> LayerDropCommand { get; } = new DelegateCommand<(object sender, EventArgs args)>();
        public DelegateCommand<(object sender, EventArgs args)> LayerDragOverCommand { get; } = new DelegateCommand<(object sender, EventArgs args)>();
        public DelegateCommand<object> LayerMoveCommand { get; } = new DelegateCommand<object>();


        #region Select
        public void LayerSelect(object sender) {
            var grid = (Grid)sender;
            Select_Layer = AttachmentProperty.GetInt(grid);
            Select_Frame = ToFrame(Mouse.GetPosition(grid).X);
        }
        #endregion

        #region Drop
        public void LayerDrop(object sender, EventArgs e) {
            if (e is DragEventArgs de) {
                de.Effects = DragDropEffects.Copy; // マウスカーソルをコピーにする。

                int frame = ToFrame(de.GetPosition(Scene.GetCreateTimeLineView().Layer).X);


                int addlayer = AttachmentProperty.GetInt((Grid)sender);


                if (de.Data.GetDataPresent(typeof(Type))) {
                    var command = new ClipData.Add(Scene, frame, addlayer, (Type)de.Data.GetData(typeof(Type)));
                    UndoRedoManager.Do(command);
                }
                else if (de.Data.GetDataPresent(DataFormats.FileDrop, true)) {
                    string file = (de.Data.GetData(DataFormats.FileDrop) as string[])[0];

                    if (Path.GetExtension(file) != ".beo") {
                        var type_ = DataTools.FileTypeConvert(file);
                        var a = new ClipData.Add(Scene, frame, addlayer, type_);
                        UndoRedoManager.Do(a);

                        if (type_ == ClipType.Image) {
                            ((DefaultData.Image)((ImageObject)a.data.Effect[0]).Custom).File.File = file;
                        }
                        else if (type_ == ClipType.Video) {
                            ((DefaultData.Video)((ImageObject)a.data.Effect[0]).Custom).File.File = file;
                        }
                        else if (type_ == ClipType.Text) {
                            var reader = new StreamReader(file);
                            ((DefaultData.Text)((ImageObject)a.data.Effect[0]).Custom).Document.Text = reader.ReadToEnd();
                            reader.Close();
                        }

                        Component.Current.Project.PreviewUpdate(a.data);
                    }
                }
            }
        }
        #endregion

        #region DragOver
        public void LayerDragOver(object sender, EventArgs e) {
            if (e is DragEventArgs de) {
                de.Effects = DragDropEffects.Copy;

                de.Handled = true;
            }
        }
        #endregion

        #region MouseMove
        public void LayerMouseMove(object sender) => Mouse_Layer = AttachmentProperty.GetInt((Grid)sender);
        #endregion

        #endregion

        #region ショートカット

        public DelegateCommand CopyCommand { get; }

        public DelegateCommand CutCommand { get; }

        public DelegateCommand DeleteCommand { get; }

        #endregion


        #region タイムライン操作

        public DelegateCommand<Point> TimeLineMouseLeftDownCommand { get; } = new();
        public DelegateCommand TimeLineMouseLeftUpCommand { get; } = new();
        public DelegateCommand<Point> TimeLineMouseMoveCommand { get; } = new();
        public DelegateCommand TimeLineMouseLeaveCommand { get; } = new();



        #region Loaded
        public void TimeLineLoaded(Action<ObservableCollection<ClipData>> action) {
            var from = Scene.TimeLineZoom;
            Scene.TimeLineZoom = 50;
            Scene.TimeLineZoom = from; //遠回しにviewを更新する
            
            TrackWidth.Value = ToPixel(Scene.TotalFrame);

            //目盛り追加
            ResetScale?.Invoke(Scene.TimeLineZoom, Scene.TotalFrame, Component.Current.Project.FrameRate);

            action?.Invoke(Scene.Datas);

            SeekbarMargin.Value = new Thickness(ToPixel(Scene.PreviewFrame), 0, 0, 0);
        }
        #endregion

        #region MouseLeftDown
        public void TimeLineMouseLeftDown(Point point) {
            if (ClipMouseDown || !KeyframeToggle) {
                return;
            }

            // フラグを"マウス押下中"にする
            SeekbarIsMouseDown = true;

            var s = ToFrame(point.X);

            Scene.PreviewFrame = s + 1;
        }
        #endregion

        #region MouseLeftUp
        public void TimeLineMouseLeftUp() {
            // マウス押下中フラグを落とす
            SeekbarIsMouseDown = false;
            LayerCursor.Value = Cursors.Arrow;

            //保存
            if (ClipLeftRight != 0 && ClipSelect != null) {

                int start = ToFrame(ClipSelect.GetCreateClipViewModel().MarginLeftProperty);
                int end = ToFrame(ClipSelect.GetCreateClipViewModel().WidthProperty.Value) + start;//変更後の最大フレーム

                UndoRedoManager.Do(new ClipData.LengthChange(ClipSelect, start, end));

                ClipLeftRight = 0;
            }
        }
        #endregion

        #region MouseMove
        public void TimeLineMouseMove(Point point) {
            //マウスの現在フレーム
            Point obj_Now;

            if (SeekbarIsMouseDown && KeyframeToggle) {
                var s = ToFrame(point.X);

                Scene.PreviewFrame = s + 1;
            }
            else if (ClipMouseDown) {
                var selectviewmodel = ClipSelect.GetCreateClipViewModel();
                if (selectviewmodel.ClipCursor.Value == Cursors.Arrow && LayerCursor.Value == Cursors.Arrow) {
                    #region 横の移動

                    obj_Now = point; //現在のマウス

                    var column = ToFrame(obj_Now.X) - ToFrame(ClipStart.X) + ToFrame(selectviewmodel.MarginProperty.Value.Left);

                    if (column < 0) {
                        selectviewmodel.MarginProperty.Value = new Thickness(0, 1, 0, 0);
                    }
                    else {
                        selectviewmodel.MarginProperty.Value = new Thickness(ToPixel(column), 1, 0, 0);
                    }

                    #endregion


                    #region 縦の移動

                    int tolayer = Mouse_Layer;

                    if (selectviewmodel.Row != tolayer && tolayer != 0) {
                        ClipLayerMoveCommand?.Invoke(ClipSelect, tolayer);
                    }

                    #endregion

                    ClipStart = obj_Now;

                    ClipTimeChange = true;
                }
                else {
                    obj_Now = point; //現在のマウスの位置


                    ClipMovement = ToPixel(ToFrame(obj_Now.X) - ToFrame(ClipStart.X)); //一時的な移動量

                    if (ClipLeftRight == 2) { //左
                        selectviewmodel.WidthProperty.Value += ClipMovement;
                    }
                    else if (ClipLeftRight == 1) {
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
        public void TimeLineMouseLeave() {
            SeekbarIsMouseDown = false;

            ClipMouseDown = false;
            LayerCursor.Value = Cursors.Arrow;


            if (ClipTimeChange) {
                ClipData data = ClipSelect;

                UndoRedoManager.Do(new ClipData.Move(data: data,
                                                     to: ToFrame(data.GetCreateClipViewModel().MarginLeftProperty),
                                                     tolayer: ClipSelect.GetCreateClipViewModel().Row));

                ClipTimeChange = false;
            }
        }
        #endregion

        #endregion



        #region フレーム番号を座標に変換
        public double ToPixel(int number) => Setting.WidthOf1Frame * (Scene.TimeLineZoom / 100) * number;
        #endregion

        #region 座標をフレーム番号に変換
        public int ToFrame(double pixel) => (int)(pixel / (Setting.WidthOf1Frame * (Scene.TimeLineZoom / 100)));
        #endregion
    }
}
