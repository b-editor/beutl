using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

using BEditor.ViewModels;

using Reactive.Bindings.Extensions;

namespace BEditor.Views
{
    public partial class VolumeView : UserControl
    {
        private readonly Border _leftBorder;
        private readonly Border _rightBorder;
        private readonly TextBlock _leftText;
        private readonly TextBlock _rightText;

        public VolumeView()
        {
            var vm = new VolumeViewModel(MainWindowViewModel.Current.Previewer.PreviewAudio);
            DataContext = vm;
            InitializeComponent();
            _leftBorder = this.FindControl<Border>("LeftBorder");
            _rightBorder = this.FindControl<Border>("RightBorder");
            _leftText = this.FindControl<TextBlock>("LeftText");
            _rightText = this.FindControl<TextBlock>("RightText");

            vm.Left.ObserveOnUIDispatcher().Subscribe(r =>
            {
                var border = _leftBorder;
                var p = r / -90;
                var rect = new Rect(
                    new Point(0, border.Bounds.Height * p),
                    new Point(border.Bounds.Width, border.Bounds.Height));
                border.Clip = new RectangleGeometry(rect);

                _leftText.Text = string.Format("{0:F0}", r);
                _leftText.Margin = new(0, (rect.Y > border.Bounds.Y || rect.Y < 0) ? 0 : rect.Y, 0, 0);
            });

            vm.Right.ObserveOnUIDispatcher().Subscribe(r =>
            {
                var border = _rightBorder;
                var p = r / -90;
                var rect = new Rect(
                    new Point(0, border.Bounds.Height * p),
                    new Point(border.Bounds.Width, border.Bounds.Height));
                border.Clip = new RectangleGeometry(rect);

                _rightText.Text = string.Format("{0:F0}", r);
                _rightText.Margin = new(0, (rect.Y > border.Bounds.Y || rect.Y < 0) ? 0 : rect.Y, 0, 0);
            });
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}