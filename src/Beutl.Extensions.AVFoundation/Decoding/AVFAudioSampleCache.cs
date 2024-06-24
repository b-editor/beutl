using Beutl.Collections;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using MonoMac.CoreMedia;

namespace Beutl.Extensions.AVFoundation.Decoding;

public class AVFAudioSampleCache(AVFSampleCacheOptions options)
{
    private readonly ILogger _logger = Log.CreateLogger<AVFAudioSampleCache>();

    public const int AudioSampleWaringGapCount = 1000;

    private CircularBuffer<AudioCache> _audioCircularBuffer = new(options.MaxAudioBufferSize);
    private short _nBlockAlign;

    private readonly record struct AudioCache(int StartSampleNum, CMSampleBuffer Sample, int AudioSampleCount)
    {
        public bool CopyBuffer(ref int startSample, ref int copySampleLength, ref nint buffer, short nBlockAlign)
        {
            int querySampleEndPos = startSample + copySampleLength;
            int cacheSampleEndPos = StartSampleNum + AudioSampleCount;
            // キャッシュ内に startSample位置があるかどうか
            if (StartSampleNum <= startSample && startSample < cacheSampleEndPos)
            {
                // 要求サイズがキャッシュを超えるかどうか
                if (querySampleEndPos <= cacheSampleEndPos)
                {
                    // キャッシュ内に収まる
                    int actualBufferPos = (startSample - StartSampleNum) * nBlockAlign;
                    int actualBufferSize = copySampleLength * nBlockAlign;
                    AVFSampleUtilities.SampleCopyToBuffer(Sample, buffer, actualBufferPos, actualBufferSize);

                    startSample += copySampleLength;
                    copySampleLength = 0;
                    buffer += actualBufferSize;

                    return true;
                }
                else
                {
                    // 現在のキャッシュ内のデータをコピーする
                    int actualBufferPos = (startSample - StartSampleNum) * nBlockAlign;
                    int leftSampleCount = cacheSampleEndPos - startSample;
                    int actualleftBufferSize = leftSampleCount * nBlockAlign;
                    AVFSampleUtilities.SampleCopyToBuffer(Sample, buffer, actualBufferPos, actualleftBufferSize);

                    startSample += leftSampleCount;
                    copySampleLength -= leftSampleCount;
                    buffer += actualleftBufferSize;

                    return true;
                }
            }

            return false;
        }
    }

    public void Reset(short nBlockAlign)
    {
        _nBlockAlign = nBlockAlign;
        CircularBuffer<AudioCache> old = _audioCircularBuffer;
        _audioCircularBuffer = new CircularBuffer<AudioCache>(options.MaxAudioBufferSize);
        foreach (AudioCache item in old)
        {
            item.Sample.Dispose();
        }
    }

    public void Add(int startSample, CMSampleBuffer buffer)
    {
        int lastAudioSampleNum = LastAudioSampleNumber();
        if (lastAudioSampleNum != -1)
        {
            int actualAudioSampleNum = lastAudioSampleNum + _audioCircularBuffer.Back().AudioSampleCount;
            if (Math.Abs(startSample - actualAudioSampleNum) > AudioSampleWaringGapCount)
            {
                _logger.LogWarning(
                    "sample laggin - lag: {lag} startSample: {startSample} lastAudioSampleNum: {lastAudioSampleNum}",
                    startSample - actualAudioSampleNum,
                    startSample,
                    actualAudioSampleNum);
            }

            startSample = lastAudioSampleNum + _audioCircularBuffer.Back().AudioSampleCount;
        }

        int audioSampleCount = buffer.NumSamples;

        if (_audioCircularBuffer.IsFull)
        {
            _audioCircularBuffer.Front().Sample.Dispose();
            _audioCircularBuffer.PopFront();
        }

        var audioCache = new AudioCache(startSample, buffer, audioSampleCount);
        _audioCircularBuffer.PushBack(audioCache);
    }

    public int LastAudioSampleNumber()
    {
        if (_audioCircularBuffer.Size > 0)
        {
            AudioCache prevAudioCache = _audioCircularBuffer.Back();
            return prevAudioCache.StartSampleNum;
        }

        return -1;
    }

    public bool SearchAudioSampleAndCopyBuffer(int startSample, int copySampleLength, nint buffer)
    {
        foreach (AudioCache audioCache in _audioCircularBuffer)
        {
            if (audioCache.CopyBuffer(ref startSample, ref copySampleLength, ref buffer, _nBlockAlign))
            {
                if (copySampleLength == 0)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
