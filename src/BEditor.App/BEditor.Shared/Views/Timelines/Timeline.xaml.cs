using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;

using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The User Control item template is documented at https://go.microsoft.com/fwlink/?LinkId=234236

namespace BEditor.Shared.Views.Timelines
{
    public sealed partial class Timeline : UserControl
    {
        public Timeline()
        {
            this.InitializeComponent();
            var layer_back = (Brush)Resources["AppBarItemPointerOverBackgroundThemeBrush"];

            for (int layer = 1; layer < 100; layer++)
            {
                Grid track = new Grid();

                Grid grid = new Grid()
                {
                    Margin = new Thickness(0, 1, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    AllowDrop = true,
                    Height = 25,
                    VerticalAlignment = VerticalAlignment.Top,
                    Width = 1920
                };

                grid.Background = layer_back;
                track.Children.Add(grid);
                Layer.Children.Add(track);
            }
        }
    }
}
