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
            using var stream = new FileStream(@"2020-06-26_19.11.28.png", FileMode.Open);
            using var renderer = new GraphicsContext(500, 500);
            using var image = new Image(stream);

            image
                .AreaExpansion(50, 50, 50, 50)
                .GaussianBlur(25, true)
                .Render(renderer)
                // ラムダバージョン
                .Render(
                    img => Console.WriteLine("レンダリング"),
                    () => Console.WriteLine("完了"),
                    ex => Console.WriteLine("エラー"),
                    () => Console.WriteLine("ファイナライズ"))
                .Dispose();

            
        }
    }
}