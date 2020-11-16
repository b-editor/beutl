using System;
using System.Collections.Generic;
using System.Text;

namespace BEditor.Core.Media.Decoder
{
    public class OpenCvVideoDecoder : VideoDecoder
    {
        public OpenCvVideoDecoder(string fileName) : base(fileName)
        {
            //VideoCapture = new VideoCapture(FileName);
            //Fps = (int)VideoCapture.Fps;
            //FrameCount = VideoCapture.FrameCount;
            //Width = VideoCapture.FrameWidth;
            //Height = VideoCapture.FrameHeight;
        }

        //private VideoCapture VideoCapture;

        public override int Fps { get; }

        public override int FrameCount { get; }

        public override int Width { get; }

        public override int Height { get; }

        public override void Dispose()
        {
            //VideoCapture.Dispose();
        }

        public override Image Read(int frame)
        {
            //VideoCapture.PosFrames = frame;

            //Mat mat = new Mat();
            //VideoCapture.Read(mat);

            //if (mat.Type() == MatType.CV_8UC3) {
            //    Cv2.CvtColor(mat, mat, ColorConversionCodes.BGR2BGRA);
            //}

            //return new Image(mat.CvPtr);
            return null;
        }
    }
}
