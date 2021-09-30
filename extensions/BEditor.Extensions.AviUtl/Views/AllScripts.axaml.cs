using System;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using BEditor.Controls;
using BEditor.Data;

namespace BEditor.Extensions.AviUtl.Views
{
    public partial class AllScripts : FluentWindow
    {
        private readonly DataGrid _dataGrid;

        public AllScripts()
        {
            InitializeComponent();
            _dataGrid = this.FindControl<DataGrid>("DataGrid");
            _dataGrid.AddHandler(PointerPressedEvent, DataGrid_PointerPressed, RoutingStrategies.Tunnel);
#if DEBUG
            this.AttachDevTools();
#endif
        }

        private async void DataGrid_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(_dataGrid).Properties.IsLeftButtonPressed
                && e.Source is StyledElement element
                && element.DataContext is ScriptEntry script)
            {
                await Task.Delay(10);

                var dataObject = new DataObject();
                dataObject.Set("EffectMetadata", new EffectMetadata(script.Name, () => new AnimationEffect(script), typeof(AnimationEffect)));

                // ドラッグ開始
                await DragDrop.DoDragDrop(e, dataObject, DragDropEffects.Copy);
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
