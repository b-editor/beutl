using System;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.ViewModels;

namespace BEditor.Views
{
    public sealed class Previewer : UserControl
    {
        private readonly Image _image;

        public Previewer()
        {
            InitializeComponent();
            _image = this.FindControl<Image>("image");
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (DataContext is PreviewerViewModel viewModel)
            {
                viewModel.ImageChanged += ViewModel_ImageChanged;
            }
        }

        private void ViewModel_ImageChanged(object? sender, EventArgs e)
        {
            _image.InvalidateVisual();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}