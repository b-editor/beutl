using Beutl.Audio;
using Silk.NET.OpenAL;
using AudioContext = Beutl.Audio.Platforms.OpenAL.AudioContext;

namespace Beutl.ViewModels;

// Plays through an OpenAL streaming source with two buffers. The context is current per thread,
// and the refill loop continues on pool threads after each await, so every call binds it first.
internal sealed class OpenALAudioSink : IAudioSink, IDisposable
{
    private readonly AudioContext _context;
    private readonly AudioChannelLayout _outputLayout;
    private readonly AudioChannelMatrix? _outputMatrix;
    private readonly uint[] _buffers;
    private readonly uint _source;
    // The sample count of each queued buffer, oldest first. A buffer queued without audio counts as zero.
    private readonly Queue<int> _queuedBufferSamples = new();
    private long _totalProcessedSamples;
    private int _nextUnqueued;

    public OpenALAudioSink(AudioContext context, AudioChannelLayout projectLayout)
    {
        _context = context;
        context.MakeCurrent();
        (_outputLayout, _outputMatrix) = AudioPlaybackOutput.Create(projectLayout, context.DeviceLayout);
        _buffers = context.GenBuffers(2);
        _source = context.GenSource();
    }

    public int BufferCount => _buffers.Length;

    public long PlayedSamples
    {
        get
        {
            _context.MakeCurrent();
            _context.GetSource(_source, GetSourceInteger.SampleOffset, out int sampleOffset);
            return _totalProcessedSamples + sampleOffset;
        }
    }

    public int ProcessedBufferCount
    {
        get
        {
            _context.MakeCurrent();
            _context.GetSource(_source, GetSourceInteger.BuffersProcessed, out int processed);
            return processed;
        }
    }

    // A source that runs out of queued buffers stops.
    public bool IsPlaying
    {
        get
        {
            _context.MakeCurrent();
            return _context.GetSourceState(_source) == SourceState.Playing;
        }
    }

    public void QueueBuffer(AudioBuffer? audio)
    {
        if (_nextUnqueued >= _buffers.Length)
            throw new InvalidOperationException("Every buffer is already queued.");

        _context.MakeCurrent();
        Queue(_buffers[_nextUnqueued++], audio);
    }

    public void RequeueProcessedBuffer(AudioBuffer? audio)
    {
        _context.MakeCurrent();
        uint buffer = _context.SourceUnqueueBuffer(_source);
        Queue(buffer, audio);
        // The oldest entry is the reused buffer's; Queue added the new one behind it.
        _totalProcessedSamples += _queuedBufferSamples.Dequeue();
    }

    public void ReleaseProcessedBuffers()
    {
        _context.MakeCurrent();
        _context.GetSource(_source, GetSourceInteger.BuffersProcessed, out int processed);
        while (processed > 0 && _queuedBufferSamples.Count > 0)
        {
            _context.SourceUnqueueBuffer(_source);
            _totalProcessedSamples += _queuedBufferSamples.Dequeue();
            processed--;
        }
    }

    public void Play()
    {
        _context.MakeCurrent();
        _context.SourcePlay(_source);
    }

    public void Stop()
    {
        _context.MakeCurrent();
        _context.SourceStop(_source);
    }

    public void Dispose()
    {
        _context.MakeCurrent();
        // https://hamken100.blogspot.com/2014/04/aldeletebuffersalinvalidoperation.html
        _context.Source(_source, SourceInteger.Buffer, 0);
        _context.DeleteBuffers(_buffers);
        _context.DeleteSource(_source);
    }

    private void Queue(uint buffer, AudioBuffer? audio)
    {
        int fillSamples = 0;
        if (audio != null)
        {
            using AudioBuffer? converted = _outputMatrix?.Apply(audio);
            _context.BufferData(buffer, converted ?? audio, _outputLayout);
            fillSamples = audio.SampleCount;
        }

        _context.SourceQueueBuffer(_source, buffer);
        _queuedBufferSamples.Enqueue(fillSamples);
    }
}
