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
    public class Timeline : UserControl
    {
        private readonly ScrollViewer _scrollLine;
        private readonly ScrollViewer _scrollLabel;
        private readonly StackPanel _layerLabel;
        internal readonly Grid _timelineGrid;
        private readonly ContextMenu _timelineMenu;
        private bool _isFirst = true;

        public Timeline()
        {
            InitializeComponent();

            _scrollLine = this.FindControl<ScrollViewer>("ScrollLine");
            _scrollLabel = this.FindControl<ScrollViewer>("ScrollLabel");
            _layerLabel = this.FindControl<StackPanel>("LayerLabel");
            _timelineGrid = this.FindControl<Grid>("timelinegrid");
            _timelineMenu = this.FindControl<ContextMenu>("TimelineMenu");

            throw new Exception();
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
            _layerLabel = this.FindControl<StackPanel>("LayerLabel");
            _timelineGrid = this.FindControl<Grid>("timelinegrid");
            _timelineMenu = this.FindControl<ContextMenu>("TimelineMenu");
            AddAllClip(scene.Datas);

            InitializeContextMenu();

            // レイヤー名追加for
            for (var l = 1; l <= 100; l++)
            {
                var toggle = new ToggleButton
                {
                    ContextMenu = CreateMenu(l),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Padding = new(0),
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
            }

            foreach (var l in scene.HideLayer)
            {
                ((ToggleButton)_layerLabel.Children[l - 1]).IsChecked = true;
            }

            // AddHandler
            Scene.Datas.CollectionChanged += ClipsCollectionChanged;
            _scrollLine.ScrollChanged += ScrollLine_ScrollChanged1;
            _scrollLabel.ScrollChanged += ScrollLabel_ScrollChanged;
            _timelineGrid.PointerMoved += TimelineGrid_PointerMoved;
            _timelineGrid.PointerReleased += TimelineGrid_PointerReleased;
            _timelineGrid.PointerPressed += TimelineGrid_PointerPressed;
            _timelineGrid.PointerLeave += TimelineGrid_PointerLeave;
            _timelineGrid.AddHandler(DragDrop.DragOverEvent, TimelineGrid_DragOver);
            _timelineGrid.AddHandler(DragDrop.DropEvent, TimelineGrid_Drop);
            DragDrop.SetAllowDrop(_timelineGrid, true);

            // WPF の Preview* イベント
            _scrollLine.AddHandler(PointerWheelChangedEvent, ScrollLine_PointerWheel, RoutingStrategies.Tunnel);

            viewmodel.GetLayerMousePosition = (e) => e.GetPosition(_timelineGrid);
            //viewmodel.ResetScale = ResetScale;
            viewmodel.ClipLayerMoveCommand = (clip, layer) =>
            {
                var vm = clip.GetCreateClipViewModel();
                vm.Row = layer;
                vm.MarginTop = TimelineViewModel.ToLayerPixel(layer);
            };

            if (OperatingSystem.IsWindows())
            {
                _scrollLine.GetObservable(BoundsProperty).Subscribe(_ =>
                {
                    if (VisualRoot is not Window win || win.Content is not Layoutable content) return;
                    var grid = ((Grid)_scrollLine.Content);

                    if (grid.Bounds.Width >= _scrollLine.Viewport.Width)
                    {
                        content.Margin = new(0, 0, 8, 0);
                    }
                    else
                    {
                        content.Margin = default;
                    }
                });
            }
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

        private void TimelineGrid_PointerLeave(object? sender, PointerEventArgs e)
        {
            ViewModel.PointerLeaved();
        }

        private void TimelineGrid_PointerPressed(object? sender, PointerPressedEventArgs e)
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

        private void TimelineGrid_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            var visual = (IVisual?)sender;
            var point = e.GetCurrentPoint(visual);

            if (point.Properties.PointerUpdateKind is PointerUpdateKind.LeftButtonReleased)
            {
                ViewModel.PointerLeftReleased();
            }
        }

        private void TimelineGrid_PointerMoved(object? sender, PointerEventArgs e)
        {
            ViewModel.PointerMoved(e.GetPosition((IVisual?)sender));
        }

        private void TimelineGrid_DragOver(object? sender, DragEventArgs e)
        {
            ViewModel.LayerCursor.Value = StandardCursorType.DragCopy;
            e.DragEffects = e.Data.Contains("ObjectMetadata") || (e.Data.GetFileNames()?.Any() ?? false) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private async void TimelineGrid_Drop(object? sender, DragEventArgs e)
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
                    mes.Snackbar(Strings.ClipExistsInTheSpecifiedLocation);
                    return;
                }

                if (ext is ".bobj")
                {
                    var efct = await Serialize.LoadFromFileAsync<EffectWrapper>(file);
                    if (efct?.Effect is not ObjectElement obj)
                    {
                        mes?.Snackbar(Strings.FailedToLoad);
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

        private void ScrollLine_ScrollChanged1(object? sender, ScrollChangedEventArgs e)
        {
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

                    _timelineGrid.Children.Add(item.GetCreateClipViewSafe());
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

                _timelineGrid.Children.Add(clip.GetCreateClipView());
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

        public void ScrollLine_PointerWheel(object? sender, PointerWheelEventArgs e)
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

        public void ScrollLine_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_isFirst)
            {
                _scrollLine.Offset = new(Scene.TimeLineHorizonOffset, Scene.TimeLineVerticalOffset);
                _scrollLabel.Offset = new(0, Scene.TimeLineVerticalOffset);

                _isFirst = false;
            }
            Scene.TimeLineHorizonOffset = _scrollLine.Offset.X;
            Scene.TimeLineVerticalOffset = _scrollLine.Offset.Y;
        }
    }
}