using System.IO;
using System.Threading.Tasks;
using System.Xml;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

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

        static AllScripts()
        {
            using var s = typeof(AllScripts).Assembly.GetManifestResourceStream("BEditor.Extensions.AviUtl.Resources.Lua.xshd")!;
            using var reader = XmlReader.Create(s);

            HighlightingManager.Instance.RegisterHighlighting(
                "Lua",
                new string[] { ".lua" },
                HighlightingLoader.Load(HighlightingLoader.LoadXshd(reader), HighlightingManager.Instance));
        }

        public async void ShowCode(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ScriptEntry se)
            {
                var window = new CodeWindow
                {
                    Title = $"{se.Name}{se.GroupName ?? string.Empty} {Path.GetRelativePath(Plugin.Loader.BaseDirectory, se.File)} (ReadOnly)",
                    Editor =
                    {
                        Text = se.Code
                    }
                };

                await window.ShowDialog(this);
            }
        }

        private async void DataGrid_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(_dataGrid).Properties.IsLeftButtonPressed
                && e.Source is StyledElement element
                && element.DataContext is ScriptEntry script
                && script != _dataGrid.SelectedItem as ScriptEntry)
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
