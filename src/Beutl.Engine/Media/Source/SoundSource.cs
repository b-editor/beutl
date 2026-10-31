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
    public override void ReadFrom(Uri uri)
    {
        if (!uri.IsFile) throw new NotSupportedException("Only file URIs are supported.");
        // A relink can switch back to a file that was replaced since other sources opened it, so read the
        // file switched to afresh instead of joining their readers.
        if (HasUri && Uri != uri) SharedMediaReaders.Shared.Invalidate(uri);
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
        // The file may have changed on disk, so reloading resources must not join readers opened before.
        if (HasUri) SharedMediaReaders.Shared.Invalidate(Uri);
        base.InvalidateResourceCache();
    }

    public new sealed class Resource : MediaSource.Resource
    {
        private MediaReader? _reader;
        private Uri? _loadedUri;

        public MediaTime Duration { get; private set; }

        public int SampleRate { get; private set; }

        public int NumChannels { get; private set; }

        public AudioChannelLayout ChannelLayout => MediaReader?.AudioInfo.ChannelLayout ?? AudioChannelLayout.Stereo;

        public MediaReader? MediaReader => _reader;

        public bool Read(int start, int length, [NotNullWhen(true)] out AudioBuffer? sound)
        {
            if (IsDisposed || _reader == null)
            {
                sound = null;
                return false;
            }

            return _reader.ReadAudio(start, length, out sound);
        }

        public bool Read(MediaTime start, MediaTime length, [NotNullWhen(true)] out AudioBuffer? sound)
        {
            if (IsDisposed || _reader == null)
            {
                sound = null;
                return false;
            }

            return _reader.ReadAudio(ToSamples(start), ToSamples(length), out sound);
        }

        public bool Read(MediaTime start, int length, [NotNullWhen(true)] out AudioBuffer? sound)
        {
            if (IsDisposed || _reader == null)
            {
                sound = null;
                return false;
            }

            return _reader.ReadAudio(ToSamples(start), length, out sound);
        }

        public bool Read(int start, MediaTime length, [NotNullWhen(true)] out AudioBuffer? sound)
        {
            if (IsDisposed || _reader == null)
            {
                sound = null;
                return false;
            }

            return _reader.ReadAudio(start, ToSamples(length), out sound);
        }

        private int ToSamples(MediaTime timeSpan)
        {
            // Compute in long and clamp to a valid int offset, so a time past int.MaxValue samples
            // does not wrap to a negative offset.
            long samples = timeSpan.ToSamples(SampleRate, MidpointRounding.ToZero);
            return (int)Math.Clamp(samples, 0, int.MaxValue);
        }

        public override void Reconcile(EngineObject obj, CompositionContext context, ref bool versionBumped)
        {
            base.Reconcile(obj, context, ref versionBumped);
            var soundSource = (SoundSource)obj;

            // A relink can restore the file at its original URI.
            if (soundSource.HasUri && (_loadedUri != soundSource.Uri || ReloadRequested))
            {
                _reader?.Dispose();
                _reader = null;

                try
                {
                    string path = soundSource.Uri.LocalPath;
                    // Encoding keeps its own reader; other resources share the file's readers. A media without an
                    // audio stream never serves others, which would give them a silent source.
                    _reader = context.DisableResourceShare
                        ? MediaReader.Open(path, new(MediaMode.Audio))
                        : SharedMediaReaders.Shared.Open(
                            SharedMediaReaders.Key.ForFile(path, MediaMode.Audio),
                            () => MediaReader.Open(path, new(MediaMode.Audio)),
                            opened => opened.HasAudio);
                }
                catch
                {
                    _reader = null;
                    _loadedUri = soundSource.Uri;
                    return;
                }

                // A media without an audio stream cannot expose AudioInfo, so treat it as unreadable (#2183).
                if (!_reader.HasAudio)
                {
                    _reader.Dispose();
                    _reader = null;
                    Duration = MediaTime.Zero;
                    SampleRate = 0;
                    NumChannels = 0;
                    _loadedUri = soundSource.Uri;
                    return;
                }

                Duration = MediaTime.FromSeconds(_reader.AudioInfo.Duration.ToDouble());
                SampleRate = _reader.AudioInfo.SampleRate;
                NumChannels = _reader.AudioInfo.NumChannels;
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
            _reader?.Dispose();
            _reader = null;
        }
    }
}
