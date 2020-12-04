using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

using BEditor.Core.Service;
using BEditor.Core.Media;
using BEditor.Models.Extension;

using Color = BEditor.Core.Media.Color;
using BEditor.Drawing;

namespace BEditor.Models.Services
{
    public class WPFImageRenderSevice : IImageRenderService
    {
        [return: MaybeNull]
        public Image<BGRA32> Ellipse(int width, int height, int line, Color color)
        {
            Image<BGRA32> img = null;

            if ((line != 0) && (width != 0) && (height != 0))
            {
                if (width % 2 == 1) width++;
                if (height % 2 == 1) height++;


                if (line >= Math.Min(width, height) / 2)
                    line = Math.Min(width, height) / 2;

                var min = Math.Min(width, height);

                if (line < min) min = line;
                if (min < 0) min = 0;

                UnitTestInvoker.Invoke(() =>
                {
                    Ellipse ellipse = new Ellipse()
                    {
                        Stroke = color.ToBrush(),
                        Width = width,
                        Height = height,
                        StrokeThickness = min
                    };

                    RenderOptions.SetClearTypeHint(ellipse, ClearTypeHint.Enabled);
                    RenderOptions.SetEdgeMode(ellipse, EdgeMode.Unspecified);

                    img = new(width, height);
                    ellipse.RenderToBitmap(new System.Windows.Size(width, height)).ToImage(img);
                });
            }
            return img;
        }
        
        [return: MaybeNull]
        public Image<BGRA32> Rectangle(int width, int height, int line, Color color)
        {
            Image<BGRA32> img = null;

            if ((line != 0) && (width != 0) && (height != 0))
            {
                if (width % 2 == 1) width++;
                if (height % 2 == 1) height++;


                if (line >= Math.Min(width, height) / 2)
                    line = Math.Min(width, height) / 2;

                var min = Math.Min(width, height);

                if (line < min) min = line;
                if (min < 0) min = 0;



                UnitTestInvoker.Invoke(() =>
                {
                    System.Windows.Shapes.Rectangle rectangle = new System.Windows.Shapes.Rectangle()
                    {
                        Stroke = color.ToBrush(),
                        Width = width,
                        Height = height,
                        StrokeThickness = min
                    };

                    RenderOptions.SetClearTypeHint(rectangle, ClearTypeHint.Enabled);
                    RenderOptions.SetEdgeMode(rectangle, EdgeMode.Unspecified);

                    img = new(width, height);
                    rectangle.RenderToBitmap(new System.Windows.Size(width, height)).ToImage(img);
                });
            }
            return img;
        }

        [return: MaybeNull]
        public Image<BGRA32> Text(int size, Color color, string text, FontRecord font, string style, bool rightToLeft)
        {
            if (font is null) return null;
            Image<BGRA32> img = null;

            var fontFamily = new FontFamily(font.Name);
            var flowDirection = FlowDirection.LeftToRight;
            if (rightToLeft)
            {
                flowDirection = FlowDirection.RightToLeft;
            }

            var styleEnum = style switch
            {
                "Normal" => FontStyles.Normal,
                "Bold" => FontStyles.Normal,
                "Italic" => FontStyles.Italic,
                "UnderLine" => FontStyles.Normal,
                "StrikeThrough" => FontStyles.Normal,
                _ => throw new NotImplementedException(),
            };
            var weightEnum = (style is "Bold") ? FontWeights.Bold : FontWeights.Normal;

#pragma warning disable CS0618 // 型またはメンバーが旧型式です
            var formattedText = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                flowDirection,
                new Typeface(
                    fontFamily,
                    styleEnum,
                    weightEnum,
                    FontStretches.Normal),
                size,
                Brushes.Green);
#pragma warning restore CS0618 // 型またはメンバーが旧型式です

            int width = (int)formattedText.Width;
            int height = (int)formattedText.Height;

            if (width != 0 || height != 0)
            {
                UnitTestInvoker.Invoke(() =>
                {
                    TextBlock element = new TextBlock
                    {
                        Text = text,
                        FontFamily = new FontFamily(font.Name),
                        FontStyle = styleEnum,
                        FontWeight = weightEnum,
                        FontStretch = FontStretches.Normal,
                        FontSize = size,
                        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(color.R, color.G, color.B)),
                        FlowDirection = flowDirection,
                        Style = null
                    };

                    TextOptions.SetTextFormattingMode(element, TextFormattingMode.Ideal);
                    TextOptions.SetTextRenderingMode(element, TextRenderingMode.ClearType);

                    img = new(width, height);
                    element.RenderToBitmap(new System.Windows.Size(width, height)).ToImage(img);
                });
            }

            return img;
        }
    }
}
