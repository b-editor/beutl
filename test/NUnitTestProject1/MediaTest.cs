using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Media;
using BEditor.Media.Decoder;
using BEditor.Media.Encoder;

using NUnit.Framework;

namespace NUnitTestProject1
{
    class MediaTest
    {
        //private static readonly string InputPath = "E:\\TestProject\\MediaEncode.mp4";
        //private static readonly string OutputPath = "E:\\TestProject\\MediaTest.png";

        [SetUp]
        public void Setup()
        {

        }

        [Test]
        public void FFmpegDecodeTest()
        {
            //IMediaDecoder decoder = new FFmpegDecoder(InputPath);

            //decoder.Read(10, out var image);

            //image.Encode(OutputPath);

            //image.Dispose();
        }

        [Test]
        public void FFmpegEncodeTest()
        {
            //const int width = 1000;
            //const int height = 1000;
            //const int fps = 30;
            //const string output = "E:\\TestProject\\MediaEncode.mp4";

            //IVideoEncoder encoder = new FFmpegEncoder(width, height, fps, VideoCodec.Default, output);

            //for (Frame frame = 0; frame < Frame.FromMinutes(1, fps); frame++)
            //{
            //    using var text = Image.Text(frame.Value.ToString(), new Font(@"C:\Users\yuuto.DESKTOP-S5PNIPB\AppData\Local\Microsoft\Windows\Fonts\keifont.ttf"), 100, Color.Amber);
            //    using var resized = new Image<BGRA32>(width, height);

            //    resized[Rectangle.FromLTRB(0, 0, text.Width, text.Height)] = text;

            //    encoder.Write(resized);
            //}

            //encoder.Dispose();
        }
    }
}