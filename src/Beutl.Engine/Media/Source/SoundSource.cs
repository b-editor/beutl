using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Beutl.Audio.Graph;
using Beutl.Composition;
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
        try
        {
            bool updateOnly = true;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }
        catch
        {
            try
            {
                resource.Dispose();
            }
            catch
            {
                // Preserve the acquisition failure while reclaiming any partially initialized reader.
            }

            throw;
        }
    }

    public new sealed class Resource : MediaSource.Resource
    {
        private Counter<MediaReader>? _counter;
        private TimeSpan _duration;
        private Uri? _loadedUri;
        private int _numChannels;
        private int _sampleRate;

        public TimeSpan Duration => ReadGeneratedResourceState(ref _duration);

        public int SampleRate => ReadGeneratedResourceState(ref _sampleRate);

        public int NumChannels => ReadGeneratedResourceState(ref _numChannels);

        public MediaReader? MediaReader => ReadGeneratedResourceState(
            ref _counter,
            static counter => counter?.Value);

        public bool Read(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound)
        {
            using GeneratedResourceOperationLease operation = BeginExclusiveResourceOperation();
            Counter<MediaReader>? counter = _counter;
            if (counter == null)
            {
                sound = null;
                return false;
            }

            return counter.Value.ReadAudio(start, length, out sound);
        }

        public bool Read(TimeSpan start, TimeSpan length, [NotNullWhen(true)] out Ref<IPcm>? sound)
        {
            using GeneratedResourceOperationLease operation = BeginExclusiveResourceOperation();
            Counter<MediaReader>? counter = _counter;
            if (counter == null)
            {
                sound = null;
                return false;
            }

            return counter.Value.ReadAudio(ToSamples(start), ToSamples(length), out sound);
        }

        public bool Read(TimeSpan start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound)
        {
            using GeneratedResourceOperationLease operation = BeginExclusiveResourceOperation();
            Counter<MediaReader>? counter = _counter;
            if (counter == null)
            {
                sound = null;
                return false;
            }

            return counter.Value.ReadAudio(ToSamples(start), length, out sound);
        }

        public bool Read(int start, TimeSpan length, [NotNullWhen(true)] out Ref<IPcm>? sound)
        {
            using GeneratedResourceOperationLease operation = BeginExclusiveResourceOperation();
            Counter<MediaReader>? counter = _counter;
            if (counter == null)
            {
                sound = null;
                return false;
            }

            return counter.Value.ReadAudio(start, ToSamples(length), out sound);
        }

        private int ToSamples(TimeSpan timeSpan)
        {
            // Compute in long and clamp to a valid int offset, so a time past int.MaxValue samples
            // does not wrap to a negative offset.
            long samples = AudioMath.TimeToSampleIndex(timeSpan, _sampleRate);
            return (int)Math.Clamp(samples, 0, int.MaxValue);
        }

        public override void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
        {
            var soundSource = (SoundSource)obj;
            using GeneratedResourceOperationLease operation = BeginExclusiveResourceOperation(soundSource);
            base.Update(obj, context, ref updateOnly);

            // Load media reader if URI changed
            bool retryPreviewFailureForDelivery = context.RenderIntent == RenderIntent.Delivery
                && _loadedUri == soundSource.Uri
                && _counter == null;
            if ((_loadedUri != soundSource.Uri || retryPreviewFailureForDelivery) && soundSource.HasUri)
            {
                Counter<MediaReader>? oldCounter = _counter;
                _counter = null;
                _duration = default;
                _sampleRate = 0;
                _numChannels = 0;
                oldCounter?.Release();

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
                    MediaReader? reader = null;
                    Counter<MediaReader>? acquired = null;
                    try
                    {
                        reader = MediaReader.Open(soundSource.Uri.LocalPath, new(MediaMode.Audio));
                        acquired = new Counter<MediaReader>(reader, null);
                        reader = null;
                        // DisableResourceShare 時は WeakReference を書き換えない。
                        // 他 Renderer（プレビュー側）の共有カウンタを
                        // エンコード専用カウンタで汚染してしまうため。
                        if (!context.DisableResourceShare)
                        {
                            Volatile.Write(
                                ref soundSource._mediaReaderRef,
                                new WeakReference<Counter<MediaReader>>(acquired));
                        }

                        _counter = acquired;
                        acquired = null;
                    }
                    catch
                    {
                        try
                        {
                            acquired?.Release();
                            reader?.Dispose();
                        }
                        catch
                        {
                            // Preserve the decoder/resource acquisition failure. Preview still degrades to silence,
                            // while Delivery rethrows that exact primary failure below.
                        }

                        _counter = null;
                        if (context.RenderIntent == RenderIntent.Delivery)
                            throw;

                        _loadedUri = soundSource.Uri;
                        return;
                    }
                }

                _duration = TimeSpan.FromSeconds(_counter.Value.AudioInfo.Duration.ToDouble());
                _sampleRate = _counter.Value.AudioInfo.SampleRate;
                _numChannels = _counter.Value.AudioInfo.NumChannels;
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
            Counter<MediaReader>? counter = null;
            if (disposing)
            {
                counter = _counter;
                _counter = null;
                _loadedUri = null;
                _duration = default;
                _sampleRate = 0;
                _numChannels = 0;
            }

            Exception? failure = null;
            try
            {
                counter?.Release();
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            try
            {
                base.Dispose(disposing);
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }

            ThrowIfCleanupFailed(failure);
        }
    }
}
