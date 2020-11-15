using NUnit.Framework;
using System.IO;

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
            var img = new BEditor.Core.Media.Image(stream);
            BEditor.Core.CLTest.AAA(img);
        }
    }
}