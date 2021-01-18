using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.ViewModels.PropertyControl;

namespace BEditor.Views.Properties
{
    public class SelectorPropertyView : UserControl
    {
        public SelectorPropertyView()
        {
            this.InitializeComponent();
        }   
        public SelectorPropertyView(SelectorPropertyViewModel viewModel)
        {
            DataContext = viewModel;
            this.InitializeComponent();
            this.FindControl<ComboBox>("box").SelectionChanged += SelectorPropertyView_SelectionChanged;
        }

        private void SelectorPropertyView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var vm = (SelectorPropertyViewModel)DataContext!;
            vm.Command.Execute(((ComboBox)sender!).SelectedIndex);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
