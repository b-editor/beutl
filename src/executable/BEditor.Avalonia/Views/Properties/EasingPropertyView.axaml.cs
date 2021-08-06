using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using BEditor.Data.Property.Easing;
using BEditor.ViewModels.Properties;

namespace BEditor.Views.Properties
{
    public sealed class EasingPropertyView : FluentWindow
    {
        private readonly EasingGraph _graph;
        private readonly Button _playButton;

        public EasingPropertyView()
        {
            InitializeComponent();
            _graph = this.FindControl<EasingGraph>("Graph");
            _playButton = this.FindControl<Button>("PlayButton");
        }

        public EasingPropertyView(object datacontext)
        {
            DataContext = datacontext;
            InitializeComponent();
            _graph = this.FindControl<EasingGraph>("Graph");
            _playButton = this.FindControl<Button>("PlayButton");
        }

        public void ListBox_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (DataContext is EasePropertyViewModel vm)
            {
                vm.EasingChangeCommand.Execute((EasingMetadata)e.AddedItems[0]!);
            }
        }

        public void RefleshGraph(object s, RoutedEventArgs e)
        {
            _graph.InvalidateVisual();
        }

        public async void Play(object s, RoutedEventArgs e)
        {
            _playButton.IsEnabled = false;
            try
            {
                for (var i = 0; i < 200; i++)
                {
                    _graph.Percent = i / 200f;
                    await Task.Delay(1);
                }
            }
            finally
            {
                _playButton.IsEnabled = true;
                _graph.Percent = 0;
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}