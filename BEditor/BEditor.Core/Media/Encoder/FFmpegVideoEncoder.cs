using System.Diagnostics;

namespace BEditor.Media.Encoder
{
    public class FFmpegVideoEncoder : VideoEncoder
    {
        private readonly Process Process;

        public FFmpegVideoEncoder(string fileName, int fps, int width, int height, int bitrate) : base(fileName, fps, width, height)
        {
            Bitrate = bitrate;

            Process = new Process();

            Process.StartInfo.FileName = @"ffmpeg.exe";
            Process.StartInfo.Arguments = $"-f image2pipe -i pipe:.bmp -maxrate {bitrate}k -r {fps} -an -y {fileName}";
            Process.StartInfo.CreateNoWindow = true;
            Process.StartInfo.UseShellExecute = false;
            Process.StartInfo.RedirectStandardInput = true;
            Process.StartInfo.RedirectStandardOutput = true;

            Process.Start();
        }

        public override void Write(Image mat)
        {
            //using (var ms = mat.ToMemoryStream()) {
            //    ms.WriteTo(Process.StandardInput.BaseStream);
            //}

        }

        public override void Dispose()
        {
            Process.Close();
            Process.Dispose();
        }


        public int Bitrate { get; }
    }
}
