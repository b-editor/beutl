// https://github.com/amate/MFVideoReader

using System.Diagnostics;

using Beutl.Collections;
using Beutl.Logging;

using Microsoft.Extensions.Logging;

using SharpDX.MediaFoundation;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

public record MFSampleCacheOptions(
    int MaxVideoBufferSize = 4, // あまり大きな値を設定するとReadSampleで停止する
    int MaxAudioBufferSize = 20);

public class MFSampleCache(MFSampleCacheOptions options)
{
    private readonly ILogger _logger = Log.CreateLogger<MFSampleCache>();

    public const int FrameWaringGapCount = 1;
    public const int AudioSampleWaringGapCount = 1000;

    private CircularBuffer<VideoCache> _videoCircularBuffer = new(options.MaxVideoBufferSize);
    private CircularBuffer<AudioCache> _audioCircularBuffer = new(options.MaxAudioBufferSize);
    private short _nBlockAlign;

    private readonly record struct VideoCache(int Frame, Sample Sample);

    private readonly record struct AudioCache(int StartSampleNum, Sample Sample, int AudioSampleCount)
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
                    SampleUtilities.SampleCopyToBuffer(Sample, buffer, actualBufferPos, actualBufferSize);

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
                    SampleUtilities.SampleCopyToBuffer(Sample, buffer, actualBufferPos, actualleftBufferSize);

                    startSample += leftSampleCount;
                    copySampleLength -= leftSampleCount;
                    buffer += actualleftBufferSize;

                    return true;
                }
            }

            return false;
        }
    }

    public void ResetVideo()
    {
        CircularBuffer<VideoCache> old = _videoCircularBuffer;
        _videoCircularBuffer = new CircularBuffer<VideoCache>(options.MaxVideoBufferSize);
        foreach (VideoCache item in old)
        {
            item.Sample.Dispose();
        }
    }

    public void ResetAudio(short nBlockAlign)
    {
        _nBlockAlign = nBlockAlign;
        foreach (AudioCache item in _audioCircularBuffer)
        {
            item.Sample.Dispose();
        }
        _audioCircularBuffer.Clear();
    }

    public void AddFrameSample(int frame, Sample pSample)
    {
        int lastFrameNum = LastFrameNumber();
        if (lastFrameNum != -1)
        {
            if (Math.Abs(lastFrameNum + 1 - frame) > FrameWaringGapCount)
            {
                //_logger.LogWarning("frame error - frame: {frame} actual frame: {actual}", frame, lastFrameNum + 1);
            }

            frame = lastFrameNum + 1;
        }

        if (_videoCircularBuffer.IsFull)
        {
            _videoCircularBuffer.Front().Sample.Dispose();
            _videoCircularBuffer.PopFront();
        }

        var videoCache = new VideoCache(frame, pSample);
        _videoCircularBuffer.PushBack(videoCache);
    }

    public void AddAudioSample(int startSample, Sample pSample)
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

        int totalLength = pSample.TotalLength;
        int audioSampleCount = totalLength / _nBlockAlign;
        Debug.Assert((totalLength % _nBlockAlign) == 0);

        if (_audioCircularBuffer.IsFull)
        {
            _audioCircularBuffer.Front().Sample.Dispose();
            _audioCircularBuffer.PopFront();
        }

        var audioCache = new AudioCache(startSample, pSample, audioSampleCount);
        _audioCircularBuffer.PushBack(audioCache);
    }

    public int LastFrameNumber()
    {
        if (_videoCircularBuffer.Size > 0)
        {
            VideoCache prevVideoCache = _videoCircularBuffer.Back();
            return prevVideoCache.Frame;
        }

        return -1;
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

    public Sample? SearchFrameSample(int frame)
    {
        foreach (VideoCache videoCache in _videoCircularBuffer.Reverse())
        {
            if (videoCache.Frame == frame)
            {
                return videoCache.Sample;
            }
        }

        return null;
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
