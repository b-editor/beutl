using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using BEditor.Media;

namespace BEditor.Media.Decoder
{
    public class FFmpegVideoDecoder : VideoDecoder
    {
        public FFmpegVideoDecoder(string fileName) : base(fileName)
        {
            //using (var video = new VideoCapture(fileName)) {
            //    Fps = (int)video.Fps;
            //    FrameCount = video.FrameCount;
            //    Width = video.FrameWidth;
            //    Height = video.FrameHeight;
            //}

            Frames = new Stream[FrameCount];
        }

        private Stream[] Frames;

        public override int Fps { get; }

        public override int FrameCount { get; }

        public override int Width { get; }

        public override int Height { get; }

        public override void Dispose()
        {
            Parallel.For(0, FrameCount, i =>
            {
                Frames[i]?.DisposeAsync();
            });
        }

        public override Image Read(int frame)
        {
            #region MyRegion

            /*
            var proc = new Process();

            proc.StartInfo.FileName = @"ffmpeg.exe";
            proc.StartInfo.Arguments = $"-ss {frame / Fps} -i {FileName} -vframes 1 -f image2 pipe:1";
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardInput = true;
            proc.StartInfo.RedirectStandardOutput = true;

            proc.Start();

            var bmp = new Bitmap(proc.StandardOutput.BaseStream);
            var mat = bmp.ToMat();


            proc.WaitForExit();
            proc.Dispose();
            bmp.Dispose();

            return mat;
            */
            #endregion

            //if (Frames[frame + 1] == null) {
            //    var proc = new Process() {
            //        StartInfo = {
            //            FileName = @"ffmpeg.exe",
            //            Arguments = $"-ss {(float)(frame / Fps)} -i {FileName} -vframes 1 -f image2 pipe:1",
            //            CreateNoWindow = true,
            //            UseShellExecute = false,
            //            RedirectStandardInput = true,
            //            RedirectStandardOutput = true
            //        }
            //    };

            //    proc.Start();

            //    var bitmap = new System.Drawing.Bitmap(proc.StandardOutput.BaseStream);
            //    var memory = new MemoryStream();

            //    bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
            //    Frames[frame + 1] = memory;

            //    var mat = bitmap.ToImage();

            //    proc.WaitForExit();
            //    proc.Dispose();
            //    bitmap.Dispose();

            //    return mat;
            //}

            //var bmp = new System.Drawing.Bitmap(Frames[frame + 1]);
            //var tmp = bmp.ToImage();
            //bmp.Dispose();

            //return tmp;
            return null;
        }
    }
}
