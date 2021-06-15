using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Media;
using BEditor.Media.Encoding;
using BEditor.Media.PCM;

namespace BEditor.Extensions.FFmpeg.Encoding
{
    public class AudioOutputStream : IAudioOutputStream
    {
        private readonly FileStream _stream;
        private readonly BinaryWriter _writer;
        private readonly string _videofile;
        private readonly string _pcmfile;
        private readonly bool _hasVideoStream;

        public AudioOutputStream(AudioEncoderSettings config, string file, bool hasVideoStream)
        {
            Configuration = config;
            _videofile = file;
            _pcmfile = Path.GetTempFileName();
            _stream = new(_pcmfile, FileMode.Create);
            _writer = new(_stream);
            _hasVideoStream = hasVideoStream;
        }

        public AudioEncoderSettings Configuration { get; }

        public TimeSpan CurrentDuration { get; private set; }

        public void AddFrame(Sound<StereoPCMFloat> sound)
        {
            foreach (var item in sound.Data)
            {
                _writer.Write(item.Left);
                _writer.Write(item.Right);
            }

            CurrentDuration = CurrentDuration.Add(sound.Duration);
        }

        public void Dispose()
        {
            _stream.Dispose();
            _writer.Dispose();
            var ffmpeg = FFmpegExecutable.GetExecutable();

            if (_hasVideoStream)
            {
                var wavfile = Path.ChangeExtension(Path.GetTempFileName(), "wav");
                var process = Process.Start(new ProcessStartInfo(
                    ffmpeg,
                    $"-f f32le -ar {Configuration.SampleRate} -ac 2 -i \"{_pcmfile}\" \"{wavfile}\"")
                {
                    CreateNoWindow = true
                })!;

                process.WaitForExit();

                var tmpvideo = Path.ChangeExtension(Path.GetTempFileName(), Path.GetExtension(_videofile));

                File.Copy(_videofile, tmpvideo);
                File.Delete(_videofile);

                process = Process.Start(new ProcessStartInfo(
                    ffmpeg,
                    $"-i \"{tmpvideo}\" -i \"{wavfile}\" -c:v copy -c:a aac -map 0:v:0 -map 1:a:0 \"{_videofile}\"")
                {
                    CreateNoWindow = true
                })!;

                process.WaitForExit();

                File.Delete(wavfile);
                File.Delete(tmpvideo);
            }
            else
            {
                var process = Process.Start(new ProcessStartInfo(
                    ffmpeg,
                    $"-f f32le -ar {Configuration.SampleRate} -ac 2 -i \"{_pcmfile}\" \"{_videofile}\"")
                {
                    CreateNoWindow = true
                })!;

                process.WaitForExit();
            }
            File.Delete(_pcmfile);
        }
    }
}