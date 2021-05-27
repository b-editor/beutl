using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using BEditor.Data;
using BEditor.Extensions;
using BEditor.Models;
using BEditor.Properties;
using BEditor.ViewModels.DialogContent;
using BEditor.Views.DialogContent;

using static BEditor.IMessage;

namespace BEditor.Views
{
    public partial class ObjectViewer : UserControl
    {
        private static IMessage Message => AppModel.Current.Message;

        public ObjectViewer()
        {
            InitializeComponent();
        }

        public static IEnumerable<string> Empty { get; } = Enumerable.Empty<string>();

        private async void CopyID_Click(object sender, RoutedEventArgs e)
        {
            if (this.FindControl<TreeView>("TreeView").SelectedItem is IEditingObject obj)
            {
                await Application.Current.Clipboard.SetTextAsync(obj.Id.ToString());
            }
            else
            {
                await Message.DialogAsync(string.Format(Strings.ErrorObjectViewer2, nameof(IEditingObject)));
            }
        }

        private Scene? GetScene()
        {
            if (this.FindControl<TreeView>("TreeView").SelectedItem is IChild<object> obj) return obj.GetParent<Scene>();
            else return AppModel.Current.Project.PreviewScene;
        }

        private ClipElement? GetClip()
        {
            if (this.FindControl<TreeView>("TreeView").SelectedItem is IChild<object> obj) return obj.GetParent<ClipElement>();
            else return AppModel.Current.Project.PreviewScene.SelectItem;
        }

        private EffectElement? GetEffect()
        {
            if (this.FindControl<TreeView>("TreeView").SelectedItem is IChild<object> obj) return obj.GetParent<EffectElement>();
            else return null;
        }

        public async void DeleteScene(object sender, RoutedEventArgs e)
        {
            try
            {
                var scene = GetScene();
                if (scene is null) return;
                if (scene is { SceneName: "root" })
                {
                    Message.Snackbar("RootScene ÇÕçÌèúÇ∑ÇÈÇ±Ç∆Ç™Ç≈Ç´Ç‹ÇπÇÒ");
                    return;
                }

                if (await Message.DialogAsync(
                    Strings.CommandQ1,
                    types: new ButtonType[] { ButtonType.Yes, ButtonType.No }) == ButtonType.Yes)
                {
                    scene.Parent!.PreviewScene = scene.Parent!.SceneList[0];
                    scene.Parent.SceneList.Remove(scene);
                    scene.Unload();

                    scene.ClearDisposable();
                }
            }
            catch (IndexOutOfRangeException)
            {
                Message.Snackbar(string.Format(Strings.ErrorObjectViewer1, nameof(Scene)));
            }
        }

        public void RemoveClip(object sender, RoutedEventArgs e)
        {
            try
            {
                var clip = GetClip();
                if (clip is null) return;
                clip.Parent.RemoveClip(clip).Execute();
            }
            catch (IndexOutOfRangeException)
            {
                Message.Snackbar(string.Format(Strings.ErrorObjectViewer1, nameof(ClipElement)));
            }
        }

        public void RemoveEffect(object sender, RoutedEventArgs e)
        {
            try
            {
                var effect = GetEffect();
                if (effect is null) return;
                effect.Parent!.RemoveEffect(effect).Execute();
            }
            catch (IndexOutOfRangeException)
            {
                Message.Snackbar(string.Format(Strings.ErrorObjectViewer1, nameof(EffectElement)));
            }
        }

        public async void CreateScene(object s, RoutedEventArgs e)
        {
            var dialog = new CreateScene
            {
                DataContext = new CreateSceneViewModel()
            };

            await dialog.ShowDialog((Window)VisualRoot);
        }

        public async void CreateClip(object s, RoutedEventArgs e)
        {
            var vm = new CreateClipViewModel();
            var guess = GetScene();
            if (guess is not null) vm.Scene.Value = guess;

            var dialog = new CreateClip
            {
                DataContext = vm
            };

            await dialog.ShowDialog((Window)VisualRoot);
        }

        public async void AddEffect(object s, RoutedEventArgs e)
        {
            var vm = new AddEffectViewModel();
            var guess = GetClip();
            if (guess is not null) vm.ClipId.Value = guess.Id.ToString();

            var dialog = new AddEffect
            {
                DataContext = vm
            };

            await dialog.ShowDialog((Window)VisualRoot);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}