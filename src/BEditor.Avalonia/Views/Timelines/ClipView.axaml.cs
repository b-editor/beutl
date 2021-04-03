using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.Data;
using BEditor.Extensions;
using BEditor.Models;

namespace BEditor.Views.Timelines
{
    public class ClipView : UserControl
    {
        public ClipView()
        {
            InitializeComponent();
        }
        
        public ClipView(ClipElement clip)
        {
            var viewmodel = clip.GetCreateClipViewModel();
            DataContext = viewmodel;

            InitializeComponent();


            Height = ConstantSettings.ClipHeight;

        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
