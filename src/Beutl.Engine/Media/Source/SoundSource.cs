using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Media.Decoding;

namespace Beutl.Media.Source;

[JsonConverter(typeof(SoundSourceJsonConverter))]
[SuppressResourceClassGeneration]
public sealed class SoundSource : MediaSource
{
    private WeakReference<Counter<MediaReader>>? _mediaReaderRef;

    public override void ReadFrom(Uri uri)
    {
        if (!uri.IsFile) throw new NotSupportedException("Only file URIs are supported.");

        if (HasUri && Uri != uri)
        {
            // 古い URI の Counter を別 Resource が保持していると
            // TryAddRef が成功して新 URI でも古い MediaReader を返してしまうため、
            // URI が切り替わったタイミングで共有参照を破棄する。
            Volatile.Write(ref _mediaReaderRef, null);
        }
        Uri = uri;
    }

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool versionBumped = true;
        resource.Reconcile(this, context, ref versionBumped);
        return resource;
    }

    internal override void InvalidateResourceCache()
    {
        Volatile.Write(ref _mediaReaderRef, null);
        base.InvalidateResourceCache();
    }

    public new sealed class Resource : MediaSource.Resource
    {
        private Counter<MediaReader>? _counter;
        private Uri? _loadedUri;

        public TimeSpan Duration { get; private set; }

        public int SampleRate { get; private set; }

        public int NumChannels { get; private set; }

        public MediaReader? MediaReader => _counter?.Value;

        public bool Read(int start, int length, [NotNullWhen(true)] out AudioBuffer? sound)
        {
            if (IsDisposed || _counter == null)
            {
                sound = null;
                return false;
            }

            return _counter.Value.ReadAudio(start, length, out sound);
        }

        public bool Read(TimeSpan start, TimeSpan length, [NotNullWhen(true)] out AudioBuffer? sound)
        {
            if (IsDisposed || _counter == null)
            {
                sound = null;
                return false;
            }

            return _counter.Value.ReadAudio(ToSamples(start), ToSamples(length), out sound);
        }

        public bool Read(TimeSpan start, int length, [NotNullWhen(true)] out AudioBuffer? sound)
        {
            if (IsDisposed || _counter == null)
            {
                sound = null;
                return false;
            }

            return _counter.Value.ReadAudio(ToSamples(start), length, out sound);
        }

        public bool Read(int start, TimeSpan length, [NotNullWhen(true)] out AudioBuffer? sound)
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
            // Compute in long and clamp to a valid int offset, so a time past int.MaxValue samples
            // does not wrap to a negative offset.
            long samples = AudioMath.TimeToSampleIndex(timeSpan, SampleRate);
            return (int)Math.Clamp(samples, 0, int.MaxValue);
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
