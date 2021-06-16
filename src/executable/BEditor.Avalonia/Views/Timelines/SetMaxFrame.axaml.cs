using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using BEditor.Data;

namespace BEditor.Views.Timelines
{
    public partial class SetMaxFrame : UserControl
    {
        private readonly Scene _scene;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public SetMaxFrame()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {
            InitializeComponent();
        }

        public SetMaxFrame(Scene scene)
        {
            _scene = scene;
            InitializeComponent();
            var num = this.FindControl<NumericUpDown>("Num");
            num.Value = scene.TotalFrame;
        }

        public void Cancel_Click(object s, RoutedEventArgs e)
        {
            if (VisualRoot is Window window)
            {
                window.Close();
            }
        }

        public void OK_Click(object s, RoutedEventArgs e)
        {
            var num = this.FindControl<NumericUpDown>("Num");
            _scene.TotalFrame = (Media.Frame)num.Value;
            if (VisualRoot is Window window)
            {
                window.Close();
            }
        }

        public void Toggle_Click(object s, RoutedEventArgs e)
        {
            var toggle = this.FindControl<ToggleButton>("Toggle");
            if (!toggle.IsChecked ?? false) return;
            var num = this.FindControl<NumericUpDown>("Num");
            num.Value = _scene.PreviewFrame;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}