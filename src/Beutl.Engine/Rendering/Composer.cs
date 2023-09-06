using Beutl.Animation;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

namespace Beutl.Rendering;

public class Composer : IComposer
{
    private readonly Audio.Audio _audio;
    private readonly InstanceClock _instanceClock = new();

    public Composer()
    {
        SampleRate = 44100;
        _audio = new Audio.Audio(SampleRate);
    }

    ~Composer()
    {
        if (!IsDisposed)
        {
            OnDispose(false);
            _audio.Dispose();

            IsDisposed = true;
        }
    }

    public IClock Clock => _instanceClock;

    public int SampleRate { get; }

    public bool IsDisposed { get; private set; }

    public bool IsAudioRendering { get; private set; }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            OnDispose(true);
            _audio.Dispose();
            GC.SuppressFinalize(this);

            IsDisposed = true;
        }
    }

    protected virtual void ComposeCore(Audio.Audio audio)
    {
        //RenderScene.Render(_audio);
    }

    public Pcm<Stereo32BitFloat>? Compose(TimeSpan timeSpan)
    {
        if (!IsAudioRendering)
        {
            try
            {
                IsAudioRendering = true;
                _instanceClock.AudioStartTime = timeSpan;
                ComposeCore(_audio);

                return _audio.GetPcm();
            }
            finally
            {
                IsAudioRendering = false;
            }
        }
        else
        {
            return default;
        }
    }

    protected virtual void OnDispose(bool disposing)
    {
    }
}
