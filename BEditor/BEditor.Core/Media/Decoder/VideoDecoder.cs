using System;
using System.Collections.Generic;
using System.Text;

namespace BEditor.Core.Media.Decoder
{
    public abstract class VideoDecoder : IDisposable
    {
        public VideoDecoder(string fileName)
        {
            FileName = fileName;
        }

        public string FileName { get; }
        public abstract int Fps { get; }
        public abstract int FrameCount { get; }
        public abstract int Width { get; }
        public abstract int Height { get; }

        public abstract Image Read(int frame);

        public abstract void Dispose();

        public static Image Read(int frame, VideoDecoder reader)
        {
            if (reader == null)
            {
                return null;
            }

            var source = reader.Read(frame);

            if (source == null)
            {
                return null;
            }

            return source;
        }
    }
}
