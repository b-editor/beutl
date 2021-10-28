using System;

using Avalonia;
using Avalonia.Markup.Xaml;

using BEditor.Controls;
using BEditor.ViewModels;

using Reactive.Bindings.Extensions;

namespace BEditor.Views
{
    public sealed class FontDialog : FluentWindow
    {
        public FontDialog()
        {
            InitializeComponent();
            DataContextChanged += FontDialog_DataContextChanged;
#if DEBUG
            this.AttachDevTools();
#endif
        }

        private void FontDialog_DataContextChanged(object? sender, EventArgs e)
        {
            if (DataContext is FontDialogViewModel viewModel)
            {
                viewModel.WindowClose.Subscribe(() =>
                {
                    Content = null;
                    DataContext = null;
                    Close();
                }).AddTo(viewModel._disposables);
            }
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}