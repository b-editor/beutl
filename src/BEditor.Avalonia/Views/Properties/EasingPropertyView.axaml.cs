using System;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Templates;

using BEditor.Data.Property.Easing;
using BEditor.ViewModels.Properties;

namespace BEditor.Views.Properties
{
    public class EasingPropertyView : UserControl
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