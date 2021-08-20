using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using BEditor.Data;
using BEditor.Extensions;
using BEditor.Models;
using BEditor.Properties;
using BEditor.ViewModels.Timelines;
using BEditor.Views.DialogContent;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings.Extensions;

namespace BEditor.Views.Timelines
{
    public sealed class Timeline : UserControl
    {
        private readonly ScrollViewer _scrollLine;
        private readonly ScrollViewer _scrollLabel;
        private readonly ScrollViewer _scrollScale;
        private readonly StackPanel _layerLabel;
        private readonly Panel _timelinePanel;
        private readonly Panel _scalePanel;
        private readonly ContextMenu _timelineMenu;
        private bool _isFirst = true;

        public Timeline()
        {
            InitializeComponent();

            _scrollLine = this.FindControl<ScrollViewer>("ScrollLine");
            _scrollLabel = this.FindControl<ScrollViewer>("ScrollLabel");
            _scrollScale = this.FindControl<ScrollViewer>("ScrollScale");
            _layerLabel = this.FindControl<StackPanel>("LayerLabel");
            _timelinePanel = this.FindControl<Panel>("TimelinePanel");
            _scalePanel = this.FindControl<Panel>("ScalePanel");
            _timelineMenu = this.FindControl<ContextMenu>("TimelineMenu");
        }

        public Timeline(Scene scene)
        {
            ContextMenu CreateMenu(int layer)
            {
                var contextMenu = new ContextMenu();

                var remove = new MenuItem();

                remove.SetValue(AttachmentProperty.IntProperty, layer);
                remove.Header = Strings.Remove;
                remove.Icon = new PathIcon
                {
                    Data = (Geometry)Application.Current.FindResource("Delete20Regular")!
                };

                contextMenu.Items = new MenuItem[] { remove };

                remove.Click += (s, _) =>
                {
                    var menu = (MenuItem)s!;
                    var layer = menu.GetValue(AttachmentProperty.IntProperty);

                    Scene.RemoveLayer(layer).Execute();
                };

                return contextMenu;
            }

            var viewmodel = scene.GetCreateTimelineViewModel();
            DataContext = viewmodel;
            InitializeComponent();

            _scrollLine = this.FindControl<ScrollViewer>("ScrollLine");
            _scrollLabel = this.FindControl<ScrollViewer>("ScrollLabel");
            _scrollScale = this.FindControl<ScrollViewer>("ScrollScale");
            _layerLabel = this.FindControl<StackPanel>("LayerLabel");
            _timelinePanel = this.FindControl<Panel>("TimelinePanel");
            _scalePanel = this.FindControl<Panel>("ScalePanel");
            _timelineMenu = this.FindControl<ContextMenu>("TimelineMenu");
            AddAllClip(scene.Datas);

            InitializeContextMenu();
            //LayerThinBorderBrush
            var borderBrush = BEditor.Settings.Default.LayerBorder switch
            {
                LayerBorder.None => null,
                LayerBorder.Strong => App.Current.FindResource("LayerStrongBorderBrush") as IBrush,
                LayerBorder.Thin => App.Current.FindResource("LayerThinBorderBrush") as IBrush,
                _ => null,
            };

            for (var l = 1; l <= 100; l++)
            {
                // レイヤー名追加
                var toggle = new ToggleButton
                {
                    ContextMenu = CreateMenu(l),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Padding = default,
                    Background = null,
                    BorderThickness = default,
                    Content = l,
                    Width = 200,
                    Height = ConstantSettings.ClipHeight
                };

                toggle.Click += async (s, _) =>
                {
                    if (s is not ToggleButton toggle || toggle.Content is not int layer) return;

                    if (!toggle.IsChecked ?? false)
                    {
                        Scene.HideLayer.Remove(layer);
                    }
                    else
                    {
                        Scene.HideLayer.Add(layer);
                    }

                    await Scene.Parent!.PreviewUpdateAsync();
                };

                _layerLabel.Children.Add(toggle);

                // レイヤーの線を追加
                var border = new Border
                {
                    Background = borderBrush,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Top,
                    Height = 1,
                    ZIndex = 2,
                    Margin = new Thickness(0, l * ConstantSettings.ClipHeight, 0, 0),
                    Tag = "LayerBorder",
                };

                _timelinePanel.Children.Add(border);
            }

            foreach (var l in scene.HideLayer)
            {
                ((ToggleButton)_layerLabel.Children[l - 1]).IsChecked = true;
            }

            // AddHandler
            Scene.Datas.CollectionChanged += ClipsCollectionChanged;

            _scrollLabel.ScrollChanged += ScrollLabel_ScrollChanged;
            _scrollLine.ScrollChanged += ScrollLine_ScrollChanged;
            _scrollLine.AddHandler(PointerWheelChangedEvent, ScrollLine_PointerWheel, RoutingStrategies.Tunnel);
            _scrollScale.AddHandler(PointerWheelChangedEvent, ScrollLine_PointerWheel, RoutingStrategies.Tunnel);

            _scalePanel.PointerMoved += ScalePanel_PointerMoved;
            _scalePanel.PointerReleased += ScalePanel_PointerReleased;
            _scalePanel.PointerPressed += ScalePanel_PointerPressed;
            _scalePanel.PointerLeave += ScalePanel_PointerLeave;

            _timelinePanel.PointerMoved += TimelinePanel_PointerMoved;
            _timelinePanel.PointerReleased += TimelinePanel_PointerReleased;
            _timelinePanel.PointerPressed += TimelinePanel_PointerPressed;
            _timelinePanel.PointerLeave += TimelinePanel_PointerLeave;
            _timelinePanel.AddHandler(DragDrop.DragOverEvent, TimelinePanel_DragOver);
            _timelinePanel.AddHandler(DragDrop.DropEvent, TimelinePanel_Drop);
            DragDrop.SetAllowDrop(_timelinePanel, true);

            viewmodel.GetLayerMousePosition = (e) => e.GetPosition(_timelinePanel);
            //viewmodel.ResetScale = ResetScale;
            viewmodel.ClipLayerMoveCommand = (clip, layer) =>
            {
                var vm = clip.GetCreateClipViewModel();
                vm.Row = layer;
                vm.MarginTop = TimelineViewModel.ToLayerPixel(layer);
            };

            // シークバーを自動追跡
            viewmodel.SeekbarMargin.Subscribe(margin =>
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // 編集中の場合単純な追跡
                    if (AppModel.Current.AppStatus is Status.Edit)
                    {
                        // シークバーがViewportの右側
                        if (margin.Left > _scrollLine.Viewport.Width + _scrollLine.Offset.X)
                        {
                            _scrollLine.Offset = _scrollLine.Offset.WithX(margin.Left - _scrollLine.Viewport.Width + 1);
                        }
                        else if (_scrollLine.Offset.X > margin.Left)
                        {
                            // シークバーがViewportの左側
                            _scrollLine.Offset = _scrollLine.Offset.WithX(margin.Left - 1);
                        }
                    }
                    else if (AppModel.Current.AppStatus is Status.Playing && BEditor.Settings.Default.FixSeekbar)
                    {
                        _scrollLine.Offset = _scrollLine.Offset.WithX(margin.Left - 100);
                    }
                    else if (AppModel.Current.AppStatus is Status.Playing)
                    {
                        if (margin.Left > _scrollLine.Viewport.Width + _scrollLine.Offset.X)
                        {
                            _scrollLine.Offset = _scrollLine.Offset.WithX(margin.Left - 100);
                        }
                        else if (_scrollLine.Offset.X > margin.Left)
                        {
                            _scrollLine.Offset = _scrollLine.Offset.WithX(margin.Left - _scrollLine.Viewport.Width - 100);
                        }
                    }
                });
            });
        }

        private TimelineViewModel ViewModel => (TimelineViewModel)DataContext!;

        private Scene Scene => ViewModel.Scene;

        protected override void OnInitialized()
        {
            base.OnInitialized();

            Scene.ObserveProperty(s => s.TimeLineZoom)
                .Subscribe(_ =>
                {
                    var viewmodel = ViewModel;
                    var scene = viewmodel.Scene;

                    if (scene.TimeLineZoom <= 0)
                    {
                        scene.TimeLineZoom = 1;
                        return;
                    }
                    if (scene.TimeLineZoom >= 201)
                    {
                        scene.TimeLineZoom = 200;
                        return;
                    }

                    var l = scene.TotalFrame;

                    viewmodel.TrackWidth.Value = scene.ToPixel(l);

                    for (var index = 0; index < scene.Datas.Count; index++)
                    {
                        var clip = scene.Datas[index];
                        var start = scene.ToPixel(clip.Start);
                        var length = scene.ToPixel(clip.Length);

                        var vm = clip.GetCreateClipViewModelSafe();
                        vm.MarginLeft = start;
                        vm.WidthProperty.Value = length;
                    }

                    viewmodel.SeekbarMargin.Value = new Thickness(scene.ToPixel(scene.PreviewFrame), 0, 0, 0);
                });
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void InitializeContextMenu()
        {
            var clipMenu = new MenuItem
            {
                Header = Strings.AddClip,
                Items = ObjectMetadata.LoadedObjects.Select(metadata => new MenuItem
                {
                    CommandParameter = metadata,
                    Command = ViewModel.AddClip,
                    Header = metadata.Name
                }).ToArray()
            };

            if (_timelineMenu.Items is AvaloniaList<object> list)
            {
                list.Insert(0, clipMenu);
                list.Insert(1, new Separator());
            }
        }

        private void ScalePanel_PointerLeave(object? sender, PointerEventArgs e)
        {
            ViewModel.PointerLeaved();
        }

        private void ScalePanel_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var visual = (IVisual?)sender;
            var point = e.GetCurrentPoint(visual);
            var pos = point.Position;
            var vm = ViewModel;

            vm.ClickedFrame = Scene.ToFrame(pos.X);

            if (point.Properties.IsLeftButtonPressed)
            {
                vm.PointerLeftPressed();
            }
        }

        private void ScalePanel_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            var visual = (IVisual?)sender;
            var point = e.GetCurrentPoint(visual);

            if (point.Properties.PointerUpdateKind is PointerUpdateKind.LeftButtonReleased)
            {
                ViewModel.PointerLeftReleased();
            }
        }

        private void ScalePanel_PointerMoved(object? sender, PointerEventArgs e)
        {
            ViewModel.ScalePointerMoved(e.GetPosition((IVisual?)sender));
        }

        private void TimelinePanel_PointerLeave(object? sender, PointerEventArgs e)
        {
            ViewModel.PointerLeaved();
        }

        private void TimelinePanel_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            var visual = (IVisual?)sender;
            var point = e.GetCurrentPoint(visual);
            var pos = point.Position;
            var vm = ViewModel;

            vm.ClickedLayer = TimelineViewModel.ToLayer(pos.Y);
            vm.ClickedFrame = Scene.ToFrame(pos.X);

            if (point.Properties.IsLeftButtonPressed)
            {
                vm.PointerLeftPressed();
            }
        }

        private void TimelinePanel_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            var visual = (IVisual?)sender;
            var point = e.GetCurrentPoint(visual);

            if (point.Properties.PointerUpdateKind is PointerUpdateKind.LeftButtonReleased)
            {
                ViewModel.PointerLeftReleased();
            }
        }

        private void TimelinePanel_PointerMoved(object? sender, PointerEventArgs e)
        {
            ViewModel.PointerMoved(e.GetPosition((IVisual?)sender));
        }

        private void TimelinePanel_DragOver(object? sender, DragEventArgs e)
        {
            ViewModel.LayerCursor.Value = StandardCursorType.DragCopy;
            e.DragEffects = e.Data.Contains("ObjectMetadata") || (e.Data.GetFileNames()?.Any() ?? false) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private async void TimelinePanel_Drop(object? sender, DragEventArgs e)
        {
            ViewModel.LayerCursor.Value = StandardCursorType.Arrow;
            var vm = ViewModel;

            var pt = e.GetPosition((IVisual)sender!);

            vm.ClickedFrame = Scene.ToFrame(pt.X);
            vm.ClickedLayer = TimelineViewModel.ToLayer(pt.Y);

            if (e.Data.Get("ObjectMetadata") is ObjectMetadata metadata)
            {
                vm.AddClip.Execute(metadata);
            }
            else if (e.Data.GetFileNames() is var files && (files?.Any() ?? false))
            {
                var mes = AppModel.Current.Message;
                var file = files.First();
                var ext = Path.GetExtension(file);
                if (!Scene.InRange(vm.ClickedFrame, vm.ClickedFrame + 180, vm.ClickedLayer))
                {
                    mes.Snackbar(Strings.ClipExistsInTheSpecifiedLocation, string.Empty);
                    return;
                }

                if (ext is ".bobj")
                {
                    var efct = await Serialize.LoadFromFileAsync<EffectWrapper>(file);
                    if (efct?.Effect is not ObjectElement obj)
                    {
                        mes?.Snackbar(
                            string.Format(Strings.FailedToLoad, file),
                            string.Empty,
                            IMessage.IconType.Error);
                        return;
                    }
                    obj.Load();
                    obj.UpdateId();
                    Scene.AddClip(vm.ClickedFrame, vm.ClickedLayer, obj, out _).Execute();
                }
                else
                {
                    var supportedObjects = ObjectMetadata.LoadedObjects
                        .Where(i => i.IsSupported is not null && i.CreateFromFile is not null && i.IsSupported(file))
                        .ToArray();
                    var result = supportedObjects.FirstOrDefault();

                    if (supportedObjects.Length > 1)
                    {
                        var dialog = new SelectObjectMetadata
                        {
                            Metadatas = supportedObjects,
                            Selected = result,
                        };

                        result = await dialog.ShowDialog<ObjectMetadata?>((Window)VisualRoot!);
                    }

                    if (result is not null)
                    {
                        Scene.AddClip(vm.ClickedFrame, vm.ClickedLayer, result.CreateFromFile!.Invoke(file), out _).Execute();
                    }
                }
            }
        }

        private void ScrollLine_ScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            if (_isFirst)
            {
                _scrollLine.Offset = new(Scene.TimeLineHorizonOffset, Scene.TimeLineVerticalOffset);
                _scrollLabel.Offset = new(0, Scene.TimeLineVerticalOffset);

                _isFirst = false;
            }

            Scene.TimeLineHorizonOffset = _scrollLine.Offset.X;
            Scene.TimeLineVerticalOffset = _scrollLine.Offset.Y;

            _scrollScale.Offset = new(_scrollLine.Offset.X, 0);
            _scrollLabel.Offset = _scrollLabel.Offset.WithY(_scrollLine.Offset.Y);
        }

        private void ScrollLabel_ScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            _scrollLine.Offset = _scrollLine.Offset.WithY(_scrollLabel.Offset.Y);
        }

        private async void ClipsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (e.Action == NotifyCollectionChangedAction.Add)
                {
                    var item = Scene.Datas[e.NewStartingIndex];

                    _timelinePanel.Children.Add(item.GetCreateClipViewSafe());
                }
                else if (e.Action == NotifyCollectionChangedAction.Remove)
                {
                    var item = e.OldItems![0];

                    if (item is ClipElement clip)
                    {
                        var view = clip.GetCreateClipViewSafe();
                        (view.Parent as Grid)?.Children?.Remove(view);

                        clip.ClearDisposable();

                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                    }
                }
            });
        }

        private void AddAllClip(IList<ClipElement> clips)
        {
            for (var i = 0; i < clips.Count; i++)
            {
                var clip = clips[i];

                _timelinePanel.Children.Add(clip.GetCreateClipView());
            }
        }

        public async void SceneSettings(object s, RoutedEventArgs e)
        {
            var vm = new SceneSettingsViewModel(Scene);
            var dialog = new SceneSettings { DataContext = vm };
            await dialog.ShowDialog(App.GetMainWindow());
        }

        public async void SetMaxFrame(object s, RoutedEventArgs e)
        {
            var ctr = new SetMaxFrame(Scene);
            var dialog = new EmptyDialog(ctr);
            await dialog.ShowDialog(App.GetMainWindow());
        }

        public void UpdateLayerBorderColor()
        {
            var borderBrush = BEditor.Settings.Default.LayerBorder switch
            {
                LayerBorder.None => null,
                LayerBorder.Strong => App.Current.FindResource("LayerStrongBorderBrush") as IBrush,
                LayerBorder.Thin => App.Current.FindResource("LayerThinBorderBrush") as IBrush,
                _ => null,
            };

            foreach (var item in _timelinePanel.Children.OfType<Border>().Where(i => i.Tag is string tag && tag == "LayerBorder"))
            {
                item.Background = borderBrush;
            }
        }

        private void ScrollLine_PointerWheel(object? sender, PointerWheelEventArgs e)
        {
            if (e.KeyModifiers is KeyModifiers.Control)
            {
                var offset = _scrollLine.Offset.X;
                var frame = Scene.ToFrame(offset);
                Scene.TimeLineZoom += (float)(e.Delta.Y / 120) * 5 * Scene.TimeLineZoom;

                _scrollLine.Offset = _scrollLine.Offset.WithX(Scene.ToPixel(frame));
            }
            else if (e.Delta.Y > 0)
            {
                for (var i = 0; i < 4; i++)
                {
                    _scrollLine.LineLeft();
                }
            }
            else
            {
                for (var i = 0; i < 4; i++)
                {
                    _scrollLine.LineRight();
                }
            }

            e.Handled = true;
        }
    }
}