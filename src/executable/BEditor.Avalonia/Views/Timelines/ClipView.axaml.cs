using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Extensions;
using BEditor.Media;
using BEditor.Models;
using BEditor.ViewModels.Timelines;

namespace BEditor.Views.Timelines
{
    public class ClipVolumeView : Control
    {
        private readonly AudioObject _audio;
        private readonly ClipView _view;
        private readonly IBrush _brush = ConstantSettings.UseDarkMode ? Brushes.White : Brushes.Black;
        private readonly IBrush _background = (IBrush)Application.Current.FindResource("SystemControlBackgroundChromeBlackLowBrush")!;

        public ClipVolumeView(AudioObject audio, ClipView view)
        {
            _audio = audio;
            _view = view;
            Height = ConstantSettings.ClipHeight;
        }

        public override void Render(DrawingContext context)
        {
            var bounds = Bounds;
            context.FillRectangle(_background, new(0, 0, bounds.Width, bounds.Height));
            if (!double.IsNaN(_view.Height)) return;
            var length = (int)_audio.Parent.Length;

            for (var i = 0; i < length; i++)
            {
                var abs = i + _audio.Parent.Start;
                var sound = _audio.OnSample(new(abs, ApplyType.Audio));
                if (sound is null) continue;
                var (Left, Right) = sound.RMS();
                sound.Dispose();
                var value = (Left + Right) / 2 / -90;

                var height = Math.Clamp(bounds.Height * value, 0, bounds.Height);
                context.FillRectangle(
                    _brush,
                    new Rect(_audio.Parent.Parent.ToPixel(i), height, ConstantSettings.WidthOf1Frame, bounds.Height - height));
            }
        }
    }

    public class ClipView : UserControl, IDisposable
    {
        public ClipView()
        {
            InitializeComponent();
        }

        public ClipView(ClipElement clip)
        {
            var viewmodel = clip.GetCreateClipViewModel();
            DataContext = viewmodel;

            InitializeComponent();

            this.FindControl<Border>("border").Height = ConstantSettings.ClipHeight;
            Height = ConstantSettings.ClipHeight;

            if (clip.Effect[0] is AudioObject audio && Content is StackPanel stack)
            {
                stack.Children.Insert(1, new ClipVolumeView(audio, this));
            }
        }

        ~ClipView()
        {
            Dispatcher.UIThread.InvokeAsync(Dispose);
        }

        public ClipViewModel ViewModel => (DataContext as ClipViewModel)!;

        public void Double_Tapped(object s, RoutedEventArgs e)
        {
            var panel = Parent as Panel;

            panel?.Children.Remove(this);
            if (double.IsNaN(Height))
            {
                Height = ConstantSettings.ClipHeight;
                panel?.Children.Insert(0, this);
            }
            else
            {
                Height = double.NaN;
                panel?.Children.Add(this);
            }

            ViewModel.PointerLeftReleased();
        }

        public void Pointer_Pressed(object s, PointerPressedEventArgs e)
        {
            var point = e.GetCurrentPoint((IVisual)s);
            if (point.Properties.IsLeftButtonPressed)
            {
                ViewModel.PointerLeftPressed(e, point.Position);
            }
            else if (point.Properties.IsRightButtonPressed)
            {
                ViewModel.PointerRightPressed(point.Position);
            }
        }

        public void Pointer_Released(object s, PointerReleasedEventArgs e)
        {
            var point = e.GetCurrentPoint((IVisual)s);
            if (point.Properties.PointerUpdateKind is PointerUpdateKind.LeftButtonReleased)
            {
                ViewModel.PointerLeftReleased();
            }
        }

        public void Pointer_Moved(object s, PointerEventArgs e)
        {
            var point = e.GetCurrentPoint((IVisual)s);
            ViewModel.PointerMoved(point.Position);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
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
    }
}