using Beutl.Editor.Components.AgentTab;
using Silk.NET.OpenAL;

namespace Beutl.AgentHost;

internal sealed class AgentNotificationSoundPlayer : IAgentNotificationSoundPlayer
{
    private const int SampleRate = 24_000;
    private static readonly short[] s_tone = CreateTone();

    public Task PlayAsync(CancellationToken cancellationToken)
        => Environment.UserInteractive ? Task.Run(() => PlayTone(cancellationToken), cancellationToken) : Task.CompletedTask;

    private static unsafe void PlayTone(CancellationToken cancellationToken)
    {
        using var alc = ALContext.GetApi(soft: true);
        var device = alc.OpenDevice(null!);
        if (device is null)
            return;

        Context* context = null;
        try
        {
            // A thread-local context keeps the short notification separate from editor playback.
            var setContext = (delegate* unmanaged[Cdecl]<Context*, byte>)alc.GetProcAddress(device, "alcSetThreadContext");
            if (setContext is null)
                return;
            context = alc.CreateContext(device, null);
            if (context is null || setContext(context) == 0)
                return;
            try
            {
                using var al = AL.GetApi(soft: true);
                uint buffer = al.GenBuffer();
                uint source = al.GenSource();
                al.BufferData(buffer, BufferFormat.Mono16, s_tone, SampleRate);
                al.SetSourceProperty(source, SourceInteger.Buffer, buffer);
                al.SourcePlay(source);
                cancellationToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(240));
                al.SourceStop(source);
                al.DeleteSource(source);
                al.DeleteBuffer(buffer);
            }
            finally
            {
                setContext(null);
            }
        }
        finally
        {
            if (context is not null)
                alc.DestroyContext(context);
            alc.CloseDevice(device);
        }
    }

    private static short[] CreateTone()
    {
        var tone = new short[SampleRate * 160 / 1000];
        for (int i = 0; i < tone.Length; i++)
        {
            double progress = (double)i / tone.Length;
            double envelope = Math.Min(1, progress * 30) * Math.Pow(1 - progress, 2);
            tone[i] = (short)(short.MaxValue * 0.16 * envelope * Math.Sin(Math.Tau * 660 * i / SampleRate));
        }
        return tone;
    }
}
