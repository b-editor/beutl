using System.Reflection;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

using NUnit.Framework;

namespace DrawingTest
{
    public class PixelTest
    {
        [Test]
        public void Bgr565()
        {
            var white = Colors.White;

            var bgr565 = new Bgr565().FromColor(white);

            _ = bgr565.ToColor();
        }

        [Test]
        public void Bgra4444()
        {
            var white = Colors.White;

            var bgra4444 = new Bgra4444().FromColor(white);

            _ = bgra4444.ToColor();
        }
    }
}