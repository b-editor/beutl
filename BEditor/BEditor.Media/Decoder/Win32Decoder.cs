using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

using SharpDX.MediaFoundation;

namespace BEditor.Media.Decoder
{
    public class Win32Decoder : IVideoDecoder
    {
        private readonly SourceReader reader;
        private readonly MediaAttributes attr;
        private readonly MediaType newMediaType;

        static Win32Decoder()
        {
            MediaManager.Startup();
        }
        public Win32Decoder(string filename)
        {
            attr = new MediaAttributes(1);
            newMediaType = new MediaType();

            //SourceReaderに動画のパスを設定
            attr.Set(SourceReaderAttributeKeys.EnableVideoProcessing.Guid, true);
            reader = new SourceReader(filename, attr);

            //出力メディアタイプをRGB32bitに設定
            newMediaType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            newMediaType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
            reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, newMediaType);

            //元のメディアタイプから動画情報を取得する
            // duration:ビデオの総フレーム数
            // frameSize:フレーム画像サイズ（上位32bit:幅 下位32bit:高さ）
            // stride:フレーム画像一ライン辺りのバイト数
            var mediaType = reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
            FrameCount = (Frame)reader.GetPresentationAttribute(SourceReaderIndex.MediaSource, PresentationDescriptionAttributeKeys.Duration);
            var frameSize = mediaType.Get(MediaTypeAttributeKeys.FrameSize);
            var stride = mediaType.Get(MediaTypeAttributeKeys.DefaultStride);
            Width = (int)(frameSize >> 32);
            Height = (int)(frameSize & 0xffffffff);
        }

        public int Fps { get; }
        public Frame FrameCount { get; }
        public int Width { get; }
        public int Height { get; }

        public void Dispose()
        {
            reader.Dispose();
            attr.Dispose();
            newMediaType.Dispose();
        }
        public unsafe void Read(Frame frame, out Image<BGRA32> image)
        {
            //取得する動画の位置を設定
            reader.SetCurrentPosition(frame);

            //動画から1フレーム取得し、Bitmapオブジェクトを作成してメモリコピー
            using var sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlags.None, out int actualStreamIndex, out SourceReaderFlags readerFlags, out long timeStampRef);
            using var buf = sample.ConvertToContiguousBuffer();

            var pBuffer = buf.Lock(out int maxLength, out int currentLength);
            using var img = new Image<RGB32>(Width, Height);

            fixed (RGB32* dst = img.Data)
            {
                Buffer.MemoryCopy((void*)pBuffer, dst, img.DataSize, img.DataSize);
            }

            buf.Unlock();

            image = img.Convert<RGB32, BGRA32>();
        }

        [Serializable]
        [StructLayout(LayoutKind.Sequential)]
        [PixelFormat(3)]
        private struct RGB32 : IPixel<RGB32>, IPixelConvertable<BGRA32>
        {
            public byte B;
            public byte G;
            public byte R;
            public byte _;

            public RGB32(byte r, byte g, byte b)
            {
                R = r;
                G = g;
                B = b;
                _ = 0;
            }

            public readonly RGB32 Blend(RGB32 foreground) => foreground;
            public void ConvertFrom(BGRA32 src)
            {
                B = src.B;
                G = src.G;
                R = src.R;
            }
            public readonly void ConvertTo(out BGRA32 dst)
            {
                dst = new(R, G, B, 255);
            }
        }
    }
}
