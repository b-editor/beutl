using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

using BEditor.Data;
using BEditor.Extensions;

using Scene = BEditor.Data.Scene;

namespace BEditor.Views.Properties
{
    public sealed class PropertyPanel : Panel
    {
        public static readonly StyledProperty<Scene?> SceneProperty = AvaloniaProperty.Register<PropertyPanel, Scene?>("Scene");

        public static readonly StyledProperty<ClipElement?> SelectedClipProperty = AvaloniaProperty.Register<PropertyPanel, ClipElement?>("SelectedClip");

        public PropertyPanel()
        {
            HorizontalAlignment = HorizontalAlignment.Stretch;
            VerticalAlignment = VerticalAlignment.Stretch;
            PropertyChanged += PropertyPanel_PropertyChanged;
        }

        public Scene? Scene
        {
            get => GetValue(SceneProperty);
            set => SetValue(SceneProperty, value);
        }

        public ClipElement? SelectedClip
        {
            get => GetValue(SelectedClipProperty);
            set => SetValue(SelectedClipProperty, value);
        }

        private void PropertyPanel_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == SceneProperty)
            {
                Children.Clear();

                if (e.OldValue is Scene oldScene)
                {
                    oldScene.Datas.CollectionChanged -= Clips_CollectionChanged;
                }

                if (e.NewValue is Scene newScene)
                {
                    newScene.Datas.CollectionChanged += Clips_CollectionChanged;

                    Init(newScene);
                }
            }

            if (e.Property == SelectedClipProperty && Scene != null)
            {
                if (e.OldValue is ClipElement oldClip)
                {
                    oldClip.GetCreateClipPropertyView().IsVisible = false;
                }

                if (e.NewValue is ClipElement newClip)
                {
                    newClip.GetCreateClipPropertyView().IsVisible = true;
                }
            }
        }

        private void Init(Scene scene)
        {
            foreach (var item in scene.Datas)
            {
                var ui = item.GetCreateClipPropertyView();

                Children.Add(ui);

                ui.IsVisible = scene.SelectItem == item;
            }
        }

        private void Clips_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (Scene == null) return;
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                {
                    var item = Scene.Datas[e.NewStartingIndex];
                    var ui = item.GetCreateClipPropertyView();
                    ui.IsVisible = Scene.SelectItem == item;

                    Children.Add(ui);
                }
                else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove)
                {
                    var item = e.OldItems![0];

                    if (item is ClipElement clip)
                    {
                        var view = clip.GetCreateClipPropertyView();
                        Children.Remove(view);
                    }
                }
                else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                {
                    Children.Clear();
                }
            });
        }
    }
}
