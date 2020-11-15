using NUnit.Framework;
using System.IO;

using BEditor.Core.Media;
using BEditor.Core.Rendering;
using BEditor.Core.Rendering.Extensions;
using System;

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

            new Image(stream)
                .AreaExpansion(50, 50, 50, 50)
                .GaussianBlur(25, true)
                .Render(
                () => Console.WriteLine("レンダリング"),
                () => Console.WriteLine("完了"),
                (e) => Console.WriteLine("エラー"),
                () => Console.WriteLine("ファイナライズ"));
        }
    }
}