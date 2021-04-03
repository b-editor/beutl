using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

using BEditor.Data;
using BEditor.Extensions;
using BEditor.Media;
using BEditor.Models;
using BEditor.Views;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.Timelines
{
    public class TimelineViewModel
    {
        public int ClickedLayer;
        public Frame ClickedFrame;
        public int PointerLayer;
        public Frame PointerFrame;
        public bool SeekbarIsMouseDown;
        public bool ClipMouseDown;
        public bool KeyframeToggle = true;
        public byte ClipLeftRight = 0;
        public ClipElement? SelectedClip;
        public double ClipMovement;
        public Point ClipStart;
        public bool ClipTimeChange;

        public TimelineViewModel(Scene scene)
        {
            Scene = scene;

            TrackWidth.Value = scene.ToPixel(scene.TotalFrame);

            scene.ObserveProperty(s => s.PreviewFrame)
                .Subscribe(_ =>
                {
                    SeekbarMargin.Value = new Thickness(Scene.ToPixel(Scene.PreviewFrame), 0, 0, 0);

                    var type = RenderType.Preview;

                    if (AppModel.Current.AppStatus is Status.Playing) type = RenderType.VideoPreview;

                    Scene.Parent.PreviewUpdate(type);
                });

            scene.ObserveProperty(s => s.TotalFrame)
                .Subscribe(_ =>
                {
                    TrackWidth.Value = Scene.ToPixel(Scene.TotalFrame);

                    // Todo: 目盛り追加
                    ResetScale?.Invoke(Scene.TimeLineZoom, Scene.TotalFrame, Scene.Parent.Framerate);
                });

            LayerSelect.Subscribe(e =>
            {
                ClickedLayer = e.layer;
                ClickedFrame = e.frame;
            });

            TimelinePointerLeftPressed.Subscribe(point =>
            {
                if (ClipMouseDown || !KeyframeToggle)
                {
                    return;
                }

                // フラグを"マウス押下中"にする
                SeekbarIsMouseDown = true;

                var s = Scene.ToFrame(point.X);

                Scene.PreviewFrame = s + 1;
            });

            TimelinePointerLeftReleased.Subscribe(() =>
            {
                // マウス押下中フラグを落とす
                SeekbarIsMouseDown = false;
                LayerCursor.Value = StandardCursorType.Arrow;

                // 保存
                if (ClipLeftRight != 0 && SelectedClip != null)
                {
                    var clipVm = SelectedClip.GetCreateClipViewModel();

                    var start = Scene.ToFrame(clipVm.MarginLeft);
                    var end = Scene.ToFrame(clipVm.WidthProperty.Value) + start;

                    if (0 < start && 0 < end)
                    {
                        SelectedClip.ChangeLength(start, end).Execute();
                    }

                    ClipLeftRight = 0;
                }
            });

            TimelinePointerMoved.Subscribe(point =>
            {
                // マウスの現在フレーム
                Point obj_Now;
                PointerFrame = Scene.ToFrame(point.X);

                if (SeekbarIsMouseDown && KeyframeToggle)
                {
                    Scene.PreviewFrame = PointerFrame + 1;
                }
                else if (ClipMouseDown)
                {
                    if (SelectedClip is null) return;
                    var selectviewmodel = SelectedClip.GetCreateClipViewModel();
                    if (selectviewmodel.ClipCursor.Value == StandardCursorType.Arrow && LayerCursor.Value == StandardCursorType.Arrow)
                    {
                        //現在のマウス
                        obj_Now = point;

                        var newframe = PointerFrame - Scene.ToFrame(ClipStart.X) + Scene.ToFrame(selectviewmodel.MarginProperty.Value.Left);
                        var newlayer = PointerLayer;

                        if (!Scene.InRange(SelectedClip, newframe, newframe + SelectedClip.Length, newlayer))
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
                            selectviewmodel.MarginProperty.Value = new Thickness(Scene.ToPixel(newframe), 1, 0, 0);
                        }

                        // 縦の移動
                        if (selectviewmodel.Row != newlayer && newlayer != 0)
                        {
                            ClipLayerMoveCommand?.Invoke(SelectedClip, newlayer);
                        }

                        ClipStart = obj_Now;

                        ClipTimeChange = true;
                    }
                    else
                    {
                        obj_Now = point; //現在のマウスの位置


                        ClipMovement = Scene.ToPixel(Scene.ToFrame(obj_Now.X) - Scene.ToFrame(ClipStart.X)); //一時的な移動量

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
            });

            TimelinePointerLeaved.Subscribe(() =>
            {
                SeekbarIsMouseDown = false;

                ClipMouseDown = false;
                LayerCursor.Value = StandardCursorType.Arrow;

                if (ClipTimeChange && SelectedClip is not null)
                {
                    var clip = SelectedClip;
                    var clipVm = clip.GetCreateClipViewModel();
                    var toframe = Scene.ToFrame(clipVm.MarginLeft);

                    if (Frame.Zero > toframe)
                    {
                        clip.MoveFrameLayer(toframe, clipVm.Row).Execute();
                    }

                    ClipTimeChange = false;
                }
            });
        }

        public double TrackHeight { get; } = ConstantSettings.ClipHeight;
        public ReactiveCommand<Point> TimelinePointerMoved { get; } = new();
        public ReactiveCommand TimelinePointerLeftReleased { get; } = new();
        public ReactiveCommand<Point> TimelinePointerLeftPressed { get; } = new();
        public ReactiveCommand TimelinePointerLeaved { get; } = new();
        public ReactiveCommand<(int layer, int frame)> LayerSelect { get; } = new();
        public ReactiveCommand LayerMove { get; } = new();
        public ReactiveCommand AddClip { get; } = new();
        public ReactiveCommand Paste { get; } = new();
        public ReactiveCommand ShowSettings { get; } = new();
        public ReactiveProperty<StandardCursorType> LayerCursor { get; } = new();
        public ReactiveProperty<double> TrackWidth { get; } = new();
        public ReactivePropertySlim<Thickness> SeekbarMargin { get; } = new();
        public Scene Scene { get; }
        public Action<float, int, int>? ResetScale { get; set; }
        public Action<ClipElement, int>? ClipLayerMoveCommand { get; set; }
    }
}
