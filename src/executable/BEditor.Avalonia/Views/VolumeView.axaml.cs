using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;

using BEditor.ViewModels;

namespace BEditor.Views
{
    public partial class VolumeView : UserControl
    {
        public VolumeView()
        {
            DataContext = new VolumeViewModel(MainWindowViewModel.Current.Previewer.PreviewAudio);
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
