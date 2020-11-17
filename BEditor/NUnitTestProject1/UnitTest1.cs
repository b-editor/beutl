using NUnit.Framework;
using System.IO;

using BEditor.Core.Media;
using BEditor.Core.Renderings;
using BEditor.Core.Renderings.Extensions;
using System;
using BEditor.Core.Graphics;

namespace NUnitTestProject1
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void Test1()
        {
            const int width = 1000, height = 1000;
            using var stream = new FileStream(@"2020-06-26_19.11.28.png", FileMode.Open);
            using var renderer = new GraphicsContext(width, height);
            using var image = new Image(stream);

            image[new Rectangle(0, 0, width, height)]
                .GaussianBlur(25, true)
                .SetColor(Color.Teal)
                .Render(renderer)
                // ラムダバージョン
                .Render(
                    img => Console.WriteLine("レンダリング"),
                    () => Console.WriteLine("完了"),
                    ex => Console.WriteLine("エラー"),
                    () => Console.WriteLine("ファイナライズ"))
                .Dispose();

            using var result = new Image(width, height, ImageType.ByteCh4);
            renderer.ReadPixels(result);

            result.Save("OutImage.png");
        }
    }
}