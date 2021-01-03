using BEditor.Core.Data.Control;
using BEditor.Media;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

using NUnit.Framework;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace NUnitTestProject1
{
    public class ImageTests
    {
        private static readonly string InputPath = "E:\\TestProject\\2020-06-26_19.11.28.png";
        private static readonly string OutputPath = "E:\\TestProject\\Image\\out";

        private static string CombinePath(string file)
        {
            return Path.Combine(OutputPath, file);
        }
        [SetUp]
        public void Setup()
        {

        }

        [Test]
        public void DrawEllipse()
        {
            using var img = Image.Decode(InputPath);
            using var ellipse = Image.Ellipse(100, 100, 50, Color.Light);
            using var stream = new FileStream(CombinePath("DrawEllipse.png"), FileMode.Create);

            img.DrawImage(new Point(0, 0), ellipse);

            img.Encode(stream, EncodedImageFormat.Png);
        }
        [Test]
        public void DrawTriangle()
        {
            using var img = Image.Decode(InputPath);
            using var ellipse = Image.Polygon(3, 100, 100, Color.Light);
            using var stream = new FileStream(CombinePath("DrawTriangle.png"), FileMode.Create);

            img.DrawImage(new Point(0, 0), ellipse);

            img.Encode(stream, EncodedImageFormat.Png);
        }
        [Test]
        public void TestIndexer()
        {
            using var img = Image.Decode(InputPath);
            using var stream = new FileStream(CombinePath("TestIndexer.png"), FileMode.Create);

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
            using var img = Image.Decode(InputPath);
            using var stream = new FileStream(CombinePath("TestFlip.png"), FileMode.Create);

            img.Flip(FlipMode.X | FlipMode.Y);

            img.Encode(stream, EncodedImageFormat.Png);
        }
        [Test]
        public void TestRoundRect()
        {
            using var img = Image.Decode(InputPath);
            using var rect = Image.RoundRect(250, 250, 25, 25, 25, Color.Light);
            using var stream = new FileStream(CombinePath("TestRoundRect.png"), FileMode.Create);

            img.DrawImage(new Point(50, 50), rect);

            img.Encode(stream, EncodedImageFormat.Png);
        }
        [Test]
        public void TestDilateErode()
        {
            using var img = Image.Decode(InputPath);
            using var stream = new FileStream(CombinePath("TestDilateErode.png"), FileMode.Create);

            img.Dilate(5);
            img.Erode(5);

            img.Encode(stream, EncodedImageFormat.Png);
        }
        [Test]
        public unsafe void StackArray()
        {
            BGRA32* data = stackalloc BGRA32[100 * 100];
            using var image = new Image<BGRA32>(100, 100, data);
            using var circle = Image.Ellipse(100, 100, 25, Color.Light);

            image[new Rectangle(0, 0, circle.Width, circle.Height)] = circle;

            image.Encode(CombinePath("StackArray.png"));
        }
        [Test]
        public void ColorFormat()
        {
            var color = Color.Blue;

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