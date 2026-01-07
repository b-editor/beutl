using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media.Decoding;
using Beutl.Media.Music;

namespace Beutl.Media.Source;

[JsonConverter(typeof(SoundSourceJsonConverter))]
[SuppressResourceClassGeneration]
public sealed class SoundSource : MediaSource
{
    public SoundSource()
    {
    }

    public override void ReadFrom(Uri uri)
    {
        if (!uri.IsFile) throw new NotSupportedException("Only file URIs are supported.");

        Uri = uri;
    }

    public override Resource ToResource(RenderContext context)
    {
        var resource = new Resource();
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : MediaSource.Resource
    {
        private Ref<MediaReader>? _mediaReader;
        private Uri? _loadedUri;

        public TimeSpan Duration { get; private set; }

        public int SampleRate { get; private set; }

        public int NumChannels { get; private set; }

        public bool Read(int start, int length, [NotNullWhen(true)] out IPcm? sound)
        {
            if (IsDisposed || _mediaReader == null)
            {
                sound = null;
                return false;
            }

            return _mediaReader.Value.ReadAudio(start, length, out sound);
        }

        public bool Read(TimeSpan start, TimeSpan length, [NotNullWhen(true)] out IPcm? sound)
        {
            if (IsDisposed || _mediaReader == null)
            {
                sound = null;
                return false;
            }

            return _mediaReader.Value.ReadAudio(ToSamples(start), ToSamples(length), out sound);
        }

        public bool Read(TimeSpan start, int length, [NotNullWhen(true)] out IPcm? sound)
        {
            if (IsDisposed || _mediaReader == null)
            {
                sound = null;
                return false;
            }

            return _mediaReader.Value.ReadAudio(ToSamples(start), length, out sound);
        }

        public bool Read(int start, TimeSpan length, [NotNullWhen(true)] out IPcm? sound)
        {
            if (IsDisposed || _mediaReader == null)
            {
                sound = null;
                return false;
            }

            return _mediaReader.Value.ReadAudio(start, ToSamples(length), out sound);
        }

        private int ToSamples(TimeSpan timeSpan)
        {
            return (int)(timeSpan.TotalSeconds * SampleRate);
        }

        public override void Update(EngineObject obj, RenderContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);

            if (obj is not SoundSource soundSource)
                throw new ArgumentException("Expected SoundSource", nameof(obj));

            // Load media reader if URI changed
            if (_loadedUri != soundSource.Uri && soundSource.HasUri)
            {
                _mediaReader?.Dispose();
                var reader = MediaReader.Open(soundSource.Uri.LocalPath, new(MediaMode.Audio));
                Duration = TimeSpan.FromSeconds(reader.AudioInfo.Duration.ToDouble());
                SampleRate = reader.AudioInfo.SampleRate;
                NumChannels = reader.AudioInfo.NumChannels;
                _mediaReader = Ref<MediaReader>.Create(reader);
                _loadedUri = soundSource.Uri;

                if (!updateOnly)
                {
                    Version++;
                    updateOnly = true;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _mediaReader?.Dispose();
                _mediaReader = null;
            }
        }
    }
}
