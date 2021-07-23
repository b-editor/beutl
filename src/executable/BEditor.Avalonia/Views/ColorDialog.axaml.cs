using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using BEditor.Controls;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Extensions;
using BEditor.ViewModels.Properties;
using BEditor.Views.DialogContent;

namespace BEditor.Views
{
    public class ColorDialog : FluentWindow
    {
        internal readonly ColorPicker col;
        internal readonly Button ok_button;

        public ColorDialog()
        {
            InitializeComponent();
            col = this.FindControl<ColorPicker>("col");
            ok_button = this.FindControl<Button>("ok_button");
            Reload_Palette(null, null);
#if DEBUG
            this.AttachDevTools();
#endif
        }

        public ColorDialog(ColorPropertyViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();
            col = this.FindControl<ColorPicker>("col");
            ok_button = this.FindControl<Button>("ok_button");

            Command = _ =>
            {
                if (DataContext is ColorPropertyViewModel vm)
                {
                    var color = col.Color;
                    vm.Command.Execute((color.R, color.G, color.B, color.A));
                }
            };
            Reload_Palette(null, null);
#if DEBUG
            this.AttachDevTools();
#endif
        }

        public ColorDialog(ColorAnimationProperty viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();
            col = this.FindControl<ColorPicker>("col");
            ok_button = this.FindControl<Button>("ok_button");
            Reload_Palette(null, null);
#if DEBUG
            this.AttachDevTools();
#endif
        }

        public Action<ColorDialog>? Command { get; set; }

        public void Button_Click(object sender, RoutedEventArgs e)
        {
            if (sender == ok_button)
            {
                Command?.Invoke(this);
            }

            Content = null;
            DataContext = null;
            Close();
        }

        public void PaletteItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is KeyValuePair<string, Color> pair)
            {
                col.Color = pair.Value.ToAvalonia();
                var tab = this.FindControl<TabControl>("Tab");
                tab.SelectedIndex = 0;
            }
        }

        private async void Reload_Palette(object? s, RoutedEventArgs? e)
        {
            var paletteItems = this.FindControl<ItemsControl>("PaletteItems");
            await Task.Run(() => PaletteRegistry.Load());

            paletteItems.Items = PaletteRegistry.GetRegistered();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}