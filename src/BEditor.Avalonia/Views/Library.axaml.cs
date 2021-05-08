using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using BEditor.Data;

namespace BEditor.Views
{
    public class Library : UserControl
    {
        private readonly TreeView _tree;

        public Library()
        {
            InitializeComponent();

            _tree = this.FindControl<TreeView>("TreeView");
            _tree.AddHandler(PointerPressedEvent, TreeViewPointerPressed, RoutingStrategies.Tunnel);
        }

        private async void TreeViewPointerPressed(object? s, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(_tree).Properties.IsLeftButtonPressed)
            {
                if (_tree.SelectedItem is not EffectMetadata select || select.Type == null) return;

                await Task.Delay(10);

                var dataObject = new DataObject();
                dataObject.Set("EffectMetadata", select);

                // ドラッグ開始
                await DragDrop.DoDragDrop(e, dataObject, DragDropEffects.Copy);

                _tree.SelectedItem = null;
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}