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

using BEditor.Core.Command;
using BEditor.Core.Data;
using BEditor.Core.Data.Bindings;
using BEditor.Core.Data.Control;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions.ViewCommand;
using BEditor.Models;
using BEditor.ViewModels.CreateDialog;
using BEditor.Views.CreateDialog;

using Clipboard = System.Windows.Clipboard;

namespace BEditor.Views.ToolControl.Default
{
    /// <summary>
    /// ObjectViewer.xaml の相互作用ロジック
    /// </summary>
    public partial class ObjectViewer : UserControl
    {
        public ObjectViewer()
        {
            InitializeComponent();
        }

        public static IEnumerable<string> Empty { get; } = Array.Empty<string>();

        private void GetPath_Click(object sender, RoutedEventArgs e)
        {
            if (TreeView.SelectedItem is IBindable bindable)
            {
                var path = bindable.GetString();
                Clipboard.SetText(path);
            }
            else
            {
                Message.Snackbar(string.Format(Core.Properties.Resources.ErrorObjectViewer2, nameof(IBindable)));
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
        private Scene GetScene()
        {
            if (TreeView.SelectedItem is Scene scene) return scene;
            else if (TreeView.SelectedItem is ClipData clip) return clip.GetParent();
            else if (TreeView.SelectedItem is EffectElement effect) return effect.GetParent2();
            else if (TreeView.SelectedItem is PropertyElement property) return property.GetParent3();
            else throw new IndexOutOfRangeException();
        }
        private ClipData GetClip()
        {
            if (TreeView.SelectedItem is ClipData clip) return clip;
            else if (TreeView.SelectedItem is EffectElement effect) return effect.GetParent();
            else if (TreeView.SelectedItem is PropertyElement property) return property.GetParent2();
            else throw new IndexOutOfRangeException();
        }
        private EffectElement GetEffect()
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
                if (scene is RootScene)
                {
                    Message.Snackbar("RootScene は削除することができません");
                    return;
                }

                if (Message.Dialog(
                    Core.Properties.Resources.CommandQ1,
                    types: new ButtonType[] { ButtonType.Yes, ButtonType.No }) == ButtonType.Yes)
                {
                    scene.Parent.PreviewScene = scene.Parent.SceneList[0];
                    scene.Parent.SceneList.Remove(scene);
                    scene.Unloaded();
                }
            }
            catch (IndexOutOfRangeException)
            {
                Message.Snackbar(string.Format(Core.Properties.Resources.ErrorObjectViewer1, nameof(Scene)));
            }
        }
        private void RemoveClip(object sender, RoutedEventArgs e)
        {
            try
            {
                var clip = GetClip();
                clip.Parent.CreateRemoveCommand(clip).Execute();
            }
            catch (IndexOutOfRangeException)
            {
                Message.Snackbar(string.Format(Core.Properties.Resources.ErrorObjectViewer1, nameof(ClipData)));
            }
        }
        private void RemoveEffect(object sender, RoutedEventArgs e)
        {
            try
            {
                var effect = GetEffect();
                effect.Parent.CreateRemoveCommand(effect).Execute();
            }
            catch (IndexOutOfRangeException)
            {
                Message.Snackbar(string.Format(Core.Properties.Resources.ErrorObjectViewer1, nameof(EffectElement)));
            }
        }
        private void AddScene(object sender, RoutedEventArgs e)
        {
            new SceneCreateDialog().ShowDialog();
        }
        private void AddClip(object sender, RoutedEventArgs e)
        {
            var viewmodel = new ClipCreateDialogViewModel();
            var dialog = new ClipCreateDialog()
            {
                DataContext = viewmodel
            };

            try
            {
                var scene = GetScene();
                viewmodel.Scene.Value = scene;
            }
            finally
            {
                dialog.ShowDialog();
            }
        }
        private void AddEffect(object sender, RoutedEventArgs e)
        {
            var viewmodel = new EffectAddDialogViewModel();
            var dialog = new EffectAddDialog()
            {
                DataContext = viewmodel
            };

            try
            {
                viewmodel.Scene.Value = GetScene();
            }
            catch(IndexOutOfRangeException)
            {
                var clip = GetClip();
                viewmodel.Scene.Value = clip.Parent;
                viewmodel.TargetClip.Value = clip;
            }
            finally
            {
                dialog.ShowDialog();
            }
        }
    }
}
