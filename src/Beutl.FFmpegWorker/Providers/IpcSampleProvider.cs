using Beutl.Extensibility;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.SharedMemory;
using Beutl.FFmpegIpc.Transport;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

namespace Beutl.FFmpegWorker.Providers;

internal sealed class IpcSampleProvider : ISampleProvider
{
    private readonly IpcConnection _connection;
    private readonly SharedMemoryBuffer _audioBuffer;

    public IpcSampleProvider(IpcConnection connection, SharedMemoryBuffer audioBuffer,
        long sampleCount, long sampleRate)
    {
        _connection = connection;
        _audioBuffer = audioBuffer;
        SampleCount = sampleCount;
        SampleRate = sampleRate;
    }

    public long SampleCount { get; }
    public long SampleRate { get; }
    public long SamplesProvided { get; private set; }

    public async ValueTask<Pcm<Stereo32BitFloat>> Sample(long offset, long length)
    {
        var request = IpcMessage.Create(_connection.NextId(), MessageType.RequestSample,
            new RequestSampleMessage { Offset = offset, Length = length });
        var response = await _connection.SendAndReceiveAsync(request)
                       ?? throw new IOException("Connection closed while waiting for audio samples");

        if (response.Error != null)
            throw new InvalidOperationException($"Sample failed: {response.Error}");

        var sampleInfo = response.GetPayload<ProvideSampleMessage>()!;

        var pcm = new Pcm<Stereo32BitFloat>((int)SampleRate, sampleInfo.NumSamples);
        unsafe
        {
            _audioBuffer.Read(new Span<byte>((void*)pcm.Data, sampleInfo.DataLength));
        }

        SamplesProvided += sampleInfo.NumSamples;
        return pcm;
    }
}
