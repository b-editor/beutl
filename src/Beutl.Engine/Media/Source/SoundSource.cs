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
    private WeakReference<Counter<MediaReader>>? _mediaReaderRef;

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
        private Counter<MediaReader>? _counter;
        private Uri? _loadedUri;

        public TimeSpan Duration { get; private set; }

        public int SampleRate { get; private set; }

        public int NumChannels { get; private set; }

        public MediaReader? MediaReader => _counter?.Value;

        public bool Read(int start, int length, [NotNullWhen(true)] out IPcm? sound)
        {
            if (IsDisposed || _counter == null)
            {
                sound = null;
                return false;
            }

            return _counter.Value.ReadAudio(start, length, out sound);
        }

        public bool Read(TimeSpan start, TimeSpan length, [NotNullWhen(true)] out IPcm? sound)
        {
            if (IsDisposed || _counter == null)
            {
                sound = null;
                return false;
            }

            return _counter.Value.ReadAudio(ToSamples(start), ToSamples(length), out sound);
        }

        public bool Read(TimeSpan start, int length, [NotNullWhen(true)] out IPcm? sound)
        {
            if (IsDisposed || _counter == null)
            {
                sound = null;
                return false;
            }

            return _counter.Value.ReadAudio(ToSamples(start), length, out sound);
        }

        public bool Read(int start, TimeSpan length, [NotNullWhen(true)] out IPcm? sound)
        {
            if (IsDisposed || _counter == null)
            {
                sound = null;
                return false;
            }

            return _counter.Value.ReadAudio(start, ToSamples(length), out sound);
        }

        private int ToSamples(TimeSpan timeSpan)
        {
            return (int)(timeSpan.TotalSeconds * SampleRate);
        }

        public override void Update(EngineObject obj, RenderContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);
            var soundSource = (SoundSource)obj;

            // Load media reader if URI changed
            if (_loadedUri != soundSource.Uri && soundSource.HasUri)
            {
                _counter?.Release();
                _counter = null;
                var localRef = Volatile.Read(ref soundSource._mediaReaderRef);
                if (localRef?.TryGetTarget(out var counter) == true && counter.RefCount > 0)
                {
                    _counter = counter;
                    counter.AddRef();
                }
                else
                {
                    var reader = MediaReader.Open(soundSource.Uri.LocalPath, new(MediaMode.Audio));
                    _counter = new Counter<MediaReader>(reader, null);
                    Volatile.Write(ref soundSource._mediaReaderRef, new WeakReference<Counter<MediaReader>>(_counter));
                }

                Duration = TimeSpan.FromSeconds(_counter.Value.AudioInfo.Duration.ToDouble());
                SampleRate = _counter.Value.AudioInfo.SampleRate;
                NumChannels = _counter.Value.AudioInfo.NumChannels;
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
            _counter?.Release();
            _counter = null;
        }
    }
}
