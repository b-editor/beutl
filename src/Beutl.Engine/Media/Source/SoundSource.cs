using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Media.Decoding;
using Beutl.Media.Music;

namespace Beutl.Media.Source;

[JsonConverter(typeof(MediaSourceJsonConverter))]
[SuppressResourceClassGeneration]
public sealed class SoundSource : MediaSource
{
    private WeakReference<Counter<MediaReader>>? _mediaReaderRef;

    public override void ReadFrom(Uri uri)
    {
        if (!uri.IsFile) throw new NotSupportedException("Only file URIs are supported.");

        base.ReadFrom(uri);
    }

    public override Resource ToResource(CompositionContext context) => CreateResource<Resource>(context);

    protected override void ClearSharedResource() => Volatile.Write(ref _mediaReaderRef, null);

    public new sealed class Resource : MediaSource.Resource
    {
        private Counter<MediaReader>? _counter;
        private Uri? _loadedUri;

        public TimeSpan Duration { get; private set; }

        public int SampleRate { get; private set; }

        public int NumChannels { get; private set; }

        public MediaReader? MediaReader => _counter?.Value;

        public bool Read(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound)
        {
            if (IsDisposed || _counter == null)
            {
                sound = null;
                return false;
            }

            return _counter.Value.ReadAudio(start, length, out sound);
        }

        public override void Reconcile(EngineObject obj, CompositionContext context, ref bool versionBumped)
        {
            base.Reconcile(obj, context, ref versionBumped);
            var soundSource = (SoundSource)obj;

            // A relink can restore the file at its original URI.
            if (soundSource.HasUri && (_loadedUri != soundSource.Uri || ReloadRequested))
            {
                _counter?.Release();
                _counter = null;

                Counter<MediaReader>? shared = null;
                if (!context.DisableResourceShare)
                {
                    var localRef = Volatile.Read(ref soundSource._mediaReaderRef);
                    if (localRef?.TryGetTarget(out var counter) == true && counter.TryAddRef())
                        shared = counter;
                }

                if (shared is not null)
                {
                    _counter = shared;
                }
                else
                {
                    try
                    {
                        var reader = MediaReader.Open(soundSource.Uri.LocalPath, new(MediaMode.Audio));
                        _counter = new Counter<MediaReader>(reader, null);
                    }
                    catch
                    {
                        _counter = null;
                        _loadedUri = soundSource.Uri;
                        return;
                    }
                }

                // A media without an audio stream cannot expose AudioInfo, so treat it as unreadable (#2183) and never
                // publish its counter to the shared cache, which would give other resources a silent source.
                if (!_counter.Value.HasAudio)
                {
                    _counter.Release();
                    _counter = null;
                    Duration = TimeSpan.Zero;
                    SampleRate = 0;
                    NumChannels = 0;
                    _loadedUri = soundSource.Uri;
                    return;
                }

                // When DisableResourceShare is set, do not rewrite the WeakReference: it would
                // contaminate other renderers' (preview-side) shared counter with an
                // encode-only counter.
                if (!context.DisableResourceShare && shared is null)
                {
                    Volatile.Write(ref soundSource._mediaReaderRef, new WeakReference<Counter<MediaReader>>(_counter));
                }

                Duration = TimeSpan.FromSeconds(_counter.Value.AudioInfo.Duration.ToDouble());
                SampleRate = _counter.Value.AudioInfo.SampleRate;
                NumChannels = _counter.Value.AudioInfo.NumChannels;
                _loadedUri = soundSource.Uri;

                if (!versionBumped)
                {
                    Version++;
                    versionBumped = true;
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
