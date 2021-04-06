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

using BEditor.Data;
using BEditor.Models;

using Reactive.Bindings;

namespace BEditor.Views.ToolControl
{
    /// <summary>
    /// MoveFrame.xaml の相互作用ロジック
    /// </summary>
    public partial class MoveFrame : UserControl
    {
        private readonly Scene scene;

        public MoveFrame()
        {
            InitializeComponent();

            scene = AppData.Current.Project.PreviewScene;
            Frame.Value = (uint)AppData.Current.Project.PreviewScene.PreviewFrame.Value;
        }

        public ReactiveProperty<uint> Frame { get; } = new();

        private void CloseClick(object sender, RoutedEventArgs e)
        {
            if (Parent is ContentControl ctrl)
            {
                ctrl.Content = null;
            }
            else if (Parent is Panel panel)
            {
                panel.Children.Remove(this);
            }
        }

        private void MoveClick(object sender, RoutedEventArgs e)
        {
            scene.PreviewFrame = new((int)Frame.Value);
            AppData.Current.Project.PreviewScene = scene;
        }

        private void Text_box_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is Key.Enter)
            {
                MoveClick(sender, e);
            }
            else if (e.Key is Key.Escape)
            {
                CloseClick(sender, e);
            }
        }
    }
}
