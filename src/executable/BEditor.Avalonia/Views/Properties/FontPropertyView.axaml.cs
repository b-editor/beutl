using System;
using System.Globalization;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Primitive.Objects;
using BEditor.ViewModels;
using BEditor.ViewModels.Properties;

namespace BEditor.Views.Properties
{
    public sealed class FontPropertyView : UserControl, IDisposable
    {
        private bool _mouseDown = false;

        public FontPropertyView()
        {
            InitializeComponent();
        }

        public FontPropertyView(FontProperty property)
        {
            DataContext = new FontPropertyViewModel(property);
            InitializeComponent();
            var box = this.FindControl<ComboBox>("box");
            box.AddHandler(PointerReleasedEvent, Box_PointerReleased, RoutingStrategies.Tunnel);
            box.AddHandler(PointerPressedEvent, Box_PointerPressed, RoutingStrategies.Tunnel);

            box.PropertyChanged += static (s, e) =>
            {
                if (e.Property == ComboBox.IsDropDownOpenProperty && s is ComboBox box && box.IsDropDownOpen)
                {
                    box.IsDropDownOpen = false;
                }
            };
        }

        ~FontPropertyView()
        {
            Dispatcher.UIThread.InvokeAsync(Dispose);
        }

        public void Dispose()
        {
            if (DataContext is IDisposable disposable)
            {
                disposable.Dispose();
            }

            DataContext = null;
            GC.SuppressFinalize(this);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void Box_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            _mouseDown = true;

            e.Handled = true;
        }

        private async void Box_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_mouseDown && DataContext is FontPropertyViewModel thisViewModel)
            {
                // ダイアログ表示
                using var viewModel = new FontDialogViewModel(thisViewModel.Property.Value);

                // Textオブジェクトの場合、値を設定する
                if (thisViewModel.Property.Parent is Text textObject)
                {
                    viewModel.SampleText.Value = textObject.Document.Value;
                }
                else
                {
                    viewModel.SampleText.Value = CultureInfo.CurrentCulture.DisplayName;
                }

                var dialog = new FontDialog
                {
                    DataContext = viewModel
                };
                await dialog.ShowDialog(App.GetMainWindow());

                if (thisViewModel.Property.Value != viewModel.SelectedItem.Value.Font && viewModel.OKIsClicked)
                {
                    thisViewModel.Property.ChangeFont(viewModel.SelectedItem.Value.Font).Execute();
                }

                _mouseDown = false;
            }
        }
    }
}