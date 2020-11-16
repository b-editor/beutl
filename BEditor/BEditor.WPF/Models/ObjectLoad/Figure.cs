using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

using BEditor.Media;
using BEditor.Core;

using Pen = System.Drawing.Pen;
using BEditor.Models.Extension;

namespace BEditor.Models
{
    internal partial class ObjectLoad
    {
        internal static BEditor.Media.Image Ellipse(int width, int height, int line, Media.Color color)
        {
            BEditor.Media.Image img = null;

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

                    img = new BEditor.Media.Image(width, height);
                    ellipse.RenderToBitmap(new System.Windows.Size(width, height)).ToImage(img);
                });
            }
            return img;
        }
        internal static BEditor.Media.Image Rectangle(int width, int height, int line, Media.Color color)
        {
            BEditor.Media.Image img = null;

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

                    img = new BEditor.Media.Image(width, height);
                    rectangle.RenderToBitmap(new System.Windows.Size(width, height)).ToImage(img);
                });
            }
            return img;
        }
    }
}
