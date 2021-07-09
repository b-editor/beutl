
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using static BEditor.ViewModels.ConfigurationViewModel;

namespace BEditor.Views
{
    public sealed class PreviewerBackground : Control
    {
        public static readonly StyledProperty<BackgroundType> BackgroundTypeProperty
            = AvaloniaProperty.Register<PreviewerBackground, BackgroundType>(nameof(BackgroundType), BackgroundType.Transparent);

        static PreviewerBackground()
        {
            AffectsRender<PreviewerBackground>(BackgroundTypeProperty);
        }

        public BackgroundType BackgroundType
        {
            get => GetValue(BackgroundTypeProperty);
            set => SetValue(BackgroundTypeProperty, value);
        }

        public override void Render(DrawingContext context)
        {
            if (BackgroundType is BackgroundType.Transparent) return;
            var size = Bounds.Size;

            if (BackgroundType is BackgroundType.Black)
            {
                context.FillRectangle(Brushes.Black, new(new Point(), size));
            }
            else if (BackgroundType is BackgroundType.White)
            {
                context.FillRectangle(Brushes.White, new(new Point(), size));
            }
            else if (BackgroundType is BackgroundType.CheckDark)
            {
                DrawCheck(context, new SolidColorBrush(0xff383838), new SolidColorBrush(0xffa6a6a6), size);
            }
            else if (BackgroundType is BackgroundType.CheckLight)
            {
                DrawCheck(context, new SolidColorBrush(0xffffffff), new SolidColorBrush(0xffa8a8a8), size);
            }
        }

        private static void DrawCheck(DrawingContext context, IBrush color1, IBrush color2, Size size)
        {
            var dotsize = size.Height / 40;
            var inc = dotsize * 2;

            for (var y = 0d; y < size.Height; y += inc)
            {
                for (var x = 0d; x < size.Width; x += inc)
                {
                    context.FillRectangle(color1, new(x, y, dotsize, dotsize));
                    context.FillRectangle(color2, new(x, y + dotsize, dotsize, dotsize));

                    context.FillRectangle(color2, new(x + dotsize, y, dotsize, dotsize));
                    context.FillRectangle(color1, new(x + dotsize, y + dotsize, dotsize, dotsize));
                }
            }
        }
    }
}