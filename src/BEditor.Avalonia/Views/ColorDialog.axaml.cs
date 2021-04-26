using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using BEditor.Data.Property;
using BEditor.ViewModels.Properties;
using BEditor.Views.DialogContent;

namespace BEditor.Views
{
    public class ColorDialog : Window
    {
        internal readonly ColorPicker col;
        internal readonly Button ok_button;

        public ColorDialog()
        {
            InitializeComponent();
            col = this.FindControl<ColorPicker>("col");
            ok_button = this.FindControl<Button>("ok_button");
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
                    vm.Command.Execute((col.Red, col.Green, col.Blue, col.Alpha));
                }
            };
            col.Bind(ColorPicker.UseAlphaProperty, new Binding("Metadata.Value.UseAlpha") { Mode = BindingMode.OneTime });
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

            col.UseAlpha = viewModel.PropertyMetadata?.UseAlpha ?? false;
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

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}