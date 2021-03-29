using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Bindings;
using BEditor.Data.Property;
using BEditor.Models;
using BEditor.ViewModels.CreatePage;
using BEditor.Views.CreatePage;

using Microsoft.Extensions.DependencyInjection;

using static BEditor.IMessage;

using Clipboard = System.Windows.Clipboard;

namespace BEditor.Views.ToolControl.Default
{
    /// <summary>
    /// ObjectViewer.xaml の相互作用ロジック
    /// </summary>
    public partial class ObjectViewer : UserControl
    {
        private static readonly IMessage Message = AppData.Current.Message;

        public ObjectViewer()
        {
            InitializeComponent();
        }

        public static IEnumerable<string> Empty { get; } = Array.Empty<string>();

        private void GetPath_Click(object sender, RoutedEventArgs e)
        {
            var bindableType = typeof(IBindable<>);
            if (TreeView.SelectedItem is IPropertyElement prop)
            {
                
                var path = prop.ToString("#");
                Clipboard.SetText(path);
            }
            else
            {
                Message.Snackbar(string.Format(Properties.Resources.ErrorObjectViewer2, nameof(PropertyElement)));
            }
        }
        private void TreeView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0)
            {
                for (int i = 0; i < 4; i++)
                {
                    scrollViewer.LineUp();
                }
            }
            else
            {
                for (int i = 0; i < 4; i++)
                {
                    scrollViewer.LineDown();
                }
            }
        }
        private Scene? GetScene()
        {
            if (TreeView.SelectedItem is Scene scene) return scene;
            else if (TreeView.SelectedItem is ClipElement clip) return clip.GetParent();
            else if (TreeView.SelectedItem is EffectElement effect) return effect.GetParent2();
            else if (TreeView.SelectedItem is PropertyElement property) return property.GetParent3();
            else throw new IndexOutOfRangeException();
        }
        private ClipElement? GetClip()
        {
            if (TreeView.SelectedItem is ClipElement clip) return clip;
            else if (TreeView.SelectedItem is EffectElement effect) return effect.GetParent();
            else if (TreeView.SelectedItem is PropertyElement property) return property.GetParent2();
            else throw new IndexOutOfRangeException();
        }
        private EffectElement? GetEffect()
        {
            if (TreeView.SelectedItem is EffectElement effect) return effect;
            else if (TreeView.SelectedItem is PropertyElement property) return property.GetParent();
            else throw new IndexOutOfRangeException();
        }
        private void DeleteScene(object sender, RoutedEventArgs e)
        {
            try
            {
                var scene = GetScene();
                if (scene is null) return;
                if (scene is { SceneName: "root" })
                {
                    Message.Snackbar("RootScene は削除することができません");
                    return;
                }

                if (Message.Dialog(
                    Properties.Resources.CommandQ1,
                    types: new ButtonType[] { ButtonType.Yes, ButtonType.No }) == ButtonType.Yes)
                {
                    scene.Parent!.PreviewScene = scene.Parent!.SceneList[0];
                    scene.Parent.SceneList.Remove(scene);
                    scene.Unload();

                    scene.GetValue(ViewBuilder.TimeLineViewModelProperty)?.Dispose();

                    scene.Clear();
                }
            }
            catch (IndexOutOfRangeException)
            {
                Message.Snackbar(string.Format(Properties.Resources.ErrorObjectViewer1, nameof(Scene)));
            }
        }
        private void RemoveClip(object sender, RoutedEventArgs e)
        {
            try
            {
                var clip = GetClip();
                if (clip is null) return;
                clip.Parent.RemoveClip(clip).Execute();
            }
            catch (IndexOutOfRangeException)
            {
                Message.Snackbar(string.Format(Properties.Resources.ErrorObjectViewer1, nameof(ClipElement)));
            }
        }
        private void RemoveEffect(object sender, RoutedEventArgs e)
        {
            try
            {
                var effect = GetEffect();
                if (effect is null) return;
                effect.Parent!.RemoveEffect(effect).Execute();
            }
            catch (IndexOutOfRangeException)
            {
                Message.Snackbar(string.Format(Properties.Resources.ErrorObjectViewer1, nameof(EffectElement)));
            }
        }
        private void AddScene(object sender, RoutedEventArgs e)
        {
            var view = new SceneCreatePage();
            new NoneDialog()
            {
                Content = view,
                Owner = App.Current.MainWindow,
                MaxWidth = double.PositiveInfinity,
            }.ShowDialog();

            if (view.DataContext is IDisposable disposable) disposable.Dispose();
        }
        private void AddClip(object sender, RoutedEventArgs e)
        {
            var viewmodel = new ClipCreatePageViewModel();
            var dialog = new ClipCreatePage(viewmodel);

            try
            {
                var scene = GetScene();
                if (scene is null) return;
                viewmodel.Scene.Value = scene;
            }
            finally
            {
                new NoneDialog()
                {
                    Content = dialog,
                    MaxWidth = double.PositiveInfinity
                }.ShowDialog();

                viewmodel.Dispose();
            }
        }
        private void AddEffect(object sender, RoutedEventArgs e)
        {
            var viewmodel = new EffectAddPageViewModel();
            var dialog = new EffectAddPage(viewmodel);

            try
            {
                viewmodel.Scene.Value = GetScene() ?? throw new IndexOutOfRangeException();
            }
            catch (IndexOutOfRangeException ex)
            {
                var clip = GetClip() ?? throw ex;

                viewmodel.Scene.Value = clip.Parent;


                foreach (var i in viewmodel.ClipItems.Value)
                {
                    i.IsSelected.Value = false;
                    if (i.Clip == clip)
                    {
                        i.IsSelected.Value = true;
                    }
                }
            }
            finally
            {
                new NoneDialog()
                {
                    Content = dialog,
                    MaxWidth = double.PositiveInfinity
                }
                .ShowDialog();

                viewmodel.Dispose();
            }
        }
    }
}
