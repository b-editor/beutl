using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;

using BEditor.Controls;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Extensions;
using BEditor.Models;
using BEditor.Properties;
using BEditor.ViewModels.Properties;

using ColorPicker = FluentAvalonia.UI.Controls.ColorPicker;

namespace BEditor.Views
{
    public sealed class ColorDialog : FluentWindow
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

        public async void AddToPalette_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AddToColorPalette(Color.FromArgb(col.Color.A, col.Color.R, col.Color.G, col.Color.B));
            await dialog.ShowDialog(this);

            var paletteItems = this.FindControl<ItemsControl>("PaletteItems");
            paletteItems.Items = null;
            paletteItems.Items = PaletteRegistry.GetRegistered();
        }

        public async void Delete_PaletteItem(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuitem
                && menuitem.DataContext is KeyValuePair<string, Color> pair
                && menuitem.FindLogicalAncestorOfType<Expander>() is var expander
                && expander.DataContext is ColorPalette palette
                && palette.Colors.Remove(pair.Key, out _))
            {
                if (palette.Colors.Count is 0
                    && await AppModel.Current.Message.DialogAsync(
                        Strings.DoYouWantToDeleteThePalette,
                        types: new IMessage.ButtonType[] { IMessage.ButtonType.Yes, IMessage.ButtonType.No }) is IMessage.ButtonType.Yes)
                {
                    PaletteRegistry.RemoveRegistered(palette.Id);
                    var paletteItems = this.FindControl<ItemsControl>("PaletteItems");
                    paletteItems.Items = null;
                    paletteItems.Items = PaletteRegistry.GetRegistered();
                }
                else if (expander.Content is ItemsControl itemsControl)
                {
                    var items = itemsControl.Items;
                    itemsControl.Items = null;
                    itemsControl.Items = items;
                }

                PaletteRegistry.Save();
            }
        }

        private async void Reload_Palette(object? s, RoutedEventArgs? e)
        {
            var paletteItems = this.FindControl<ItemsControl>("PaletteItems");
            await Task.Run(() => PaletteRegistry.Load());

            paletteItems.Items = null;
            paletteItems.Items = PaletteRegistry.GetRegistered();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}