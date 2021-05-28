#define UseMemoryStream

using System;
using System.IO;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

using NUnit.Framework;

namespace NUnitTestProject1
{
    public class ImageTests
    {
        [SetUp]
        public void Setup()
        {

        }

        private static Image<BGRA32> GradentImage()
        {
            var img = new Image<BGRA32>(500, 500, new BGRA32(255, 255, 255, 255));
            img.LinerGradient(
                new PointF(0, 0),
                new PointF(1, 1),
                new Color[] { Colors.Red, Colors.Blue },
                new float[] { 0, 1 },
                ShaderTileMode.Repeat);

            return img;
        }

        [Test]
        public void DrawEllipse()
        {
            using var img = GradentImage();
            using var ellipse = Image.Ellipse(100, 100, 50, Colors.White);
#if UseMemoryStream
            using var stream = new MemoryStream();
#else
            using var stream = new FileStream("DrawEllipse.png", FileMode.Create);
#endif

            img.DrawImage(new Point(0, 0), ellipse);

            img.Encode(stream, EncodedImageFormat.Png);
        }
        [Test]
        public void AddEllipse()
        {
            using var img = GradentImage();
            using var ellipse = Image.Ellipse(100, 100, 50, Colors.White);
#if UseMemoryStream
            using var stream = new MemoryStream();
#else
            using var stream = new FileStream("AddEllipse.png", FileMode.Create);
#endif
            var rect = new Rectangle(new(0, 0), ellipse.Size);
            using var blended = img[rect];

            blended.Add(ellipse, blended);

            img[rect] = blended;

            img.Encode(stream, EncodedImageFormat.Png);
        }
        [Test]
        public void DrawTriangle()
        {
            using var img = GradentImage();
            using var ellipse = Image.Polygon(3, 100, 100, Colors.White);
#if UseMemoryStream
            using var stream = new MemoryStream();
#else
            using var stream = new FileStream("DrawTriangle.png", FileMode.Create);
#endif

            img.DrawImage(new Point(0, 0), ellipse);

            img.Encode(stream, EncodedImageFormat.Png);
        }
        [Test]
        public void TestIndexer()
        {
            using var img = GradentImage();
#if UseMemoryStream
            using var stream = new MemoryStream();
#else
            using var stream = new FileStream("TestIndexer.png", FileMode.Create);
#endif

            foreach (ref var bgra in img.Data)
            {
                var blue = bgra.B;
                var red = bgra.R;

                bgra.R = blue;
                bgra.B = red;
            }

            img.Encode(stream, EncodedImageFormat.Png);
        }
        [Test]
        public void TestFlip()
        {
            using var img = GradentImage();
#if UseMemoryStream
            using var stream = new MemoryStream();
#else
            using var stream = new FileStream("TestFlip.png", FileMode.Create);
#endif

            img.Flip(FlipMode.X | FlipMode.Y);

            img.Encode(stream, EncodedImageFormat.Png);
        }
        [Test]
        public void TestRoundRect()
        {
            using var img = GradentImage();
            using var rect = Image.RoundRect(250, 250, 25, Colors.White, 25, 25, 25, 25);
#if UseMemoryStream
            using var stream = new MemoryStream();
#else
            using var stream = new FileStream("TestRoundRect.png", FileMode.Create);
#endif

            img.DrawImage(new Point(50, 50), rect);

            img.Encode(stream, EncodedImageFormat.Png);
        }
        [Test]
        public void TestDilateErode()
        {
            using var img = GradentImage();
#if UseMemoryStream
            using var stream = new MemoryStream();
#else
            using var stream = new FileStream("TestDilateErode.png", FileMode.Create);
#endif

            img.Dilate(5);
            img.Erode(5);

            img.Encode(stream, EncodedImageFormat.Png);
        }
        [Test]
        public unsafe void StackArray()
        {
            BGRA32* data = stackalloc BGRA32[100 * 100];
            using var image = new Image<BGRA32>(100, 100, data);
            using var circle = Image.Ellipse(100, 100, 25, Colors.White);
#if UseMemoryStream
            using var stream = new MemoryStream();
#else
            using var stream = new FileStream("StackArray.png", FileMode.Create);
#endif
            image[new Rectangle(0, 0, circle.Width, circle.Height)] = circle;

            image.Encode(stream, EncodedImageFormat.Png);
        }
        [Test]
        public void ColorFormat()
        {
            var color = Colors.Blue;

            Console.WriteLine("#argb  : " + color.ToString("#argb"));
            Console.WriteLine("#rgb   : " + color.ToString("#rgb"));
            Console.WriteLine("0xargb : " + color.ToString("0xargb"));
            Console.WriteLine("0xrgb  : " + color.ToString("0xrgb"));
            Console.WriteLine("argb   : " + color.ToString("argb"));
            Console.WriteLine("rgb    : " + color.ToString("rgb"));

            // lower
            Console.WriteLine("#argb-l  : " + color.ToString("#argb-l"));
            Console.WriteLine("#rgb-l   : " + color.ToString("#rgb-l"));
            Console.WriteLine("0xargb-l : " + color.ToString("0xargb-l"));
            Console.WriteLine("0xrgb-l  : " + color.ToString("0xrgb-l"));
            Console.WriteLine("argb-l   : " + color.ToString("argb-l"));
            Console.WriteLine("rgb-l    : " + color.ToString("rgb-l"));
        }
    }
}