using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.Data.Property.Easing;
using BEditor.ViewModels.Properties;

namespace BEditor.Views.Properties
{
    public sealed class EasingPropertyView : UserControl
    {
        public EasingPropertyView()
        {
            InitializeComponent();
        }

        public EasingPropertyView(object datacontext)
        {
            DataContext = datacontext;
            InitializeComponent();
        }

        public void ListBox_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (DataContext is EasePropertyViewModel vm)
            {
                vm.EasingChangeCommand.Execute((EasingMetadata)e.AddedItems[0]!);
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}