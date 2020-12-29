using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;
using BEditor.Media.Decoder;

using NUnit.Framework;

namespace NUnitTestProject1
{
    class MediaTest
    {
        private static readonly string InputPath = "E:\\freesoft\\Bandicam\\bandicam 2020-12-29 17-16-47-434.mp4";
        private static readonly string OutputPath = "E:\\TestProject\\MediaTest.png";

        [SetUp]
        public void Setup()
        {

        }

        [Test]
        public void DecodeTest()
        {
            IVideoDecoder decoder = new Win32Decoder(InputPath);

            decoder.Read(10, out var image);

            image.Encode(OutputPath);

            image.Dispose();
        }
    }
}
