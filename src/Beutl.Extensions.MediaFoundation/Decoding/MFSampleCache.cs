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

public class MFSampleCache
{
    private readonly ILogger _logger = Log.CreateLogger<MFSampleCache>();

    public const int kMaxVideoBufferSize = 4;    // あまり大きな値を設定するとReadSampleで停止する
    public const int kMaxAudioBufferSize = 20;

    public const int kFrameWaringGapCount = 1;
    public const int kAudioSampleWaringGapCount = 1000;

    private CircularBuffer<VideoCache> m_videoCircularBuffer = new(kMaxVideoBufferSize);
    private CircularBuffer<AudioCache> m_audioCircularBuffer = new(kMaxAudioBufferSize);
    private short m_nBlockAlign;

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
        CircularBuffer<VideoCache> old = m_videoCircularBuffer;
        m_videoCircularBuffer = new CircularBuffer<VideoCache>(4);
        foreach (VideoCache item in old)
        {
            item.Sample.Dispose();
        }
    }

    public void ResetAudio(short nBlockAlign)
    {
        m_nBlockAlign = nBlockAlign;
        foreach (AudioCache item in m_audioCircularBuffer)
        {
            item.Sample.Dispose();
        }
        m_audioCircularBuffer.Clear();
    }

    public void AddFrameSample(int frame, Sample pSample)
    {
        int lastFrameNum = LastFrameNumber();
        if (lastFrameNum != -1)
        {
            if (Math.Abs(lastFrameNum + 1 - frame) > kFrameWaringGapCount)
            {
                //_logger.LogWarning("frame error - frame: {frame} actual frame: {actual}", frame, lastFrameNum + 1);
            }

            frame = lastFrameNum + 1;
        }

        if (m_videoCircularBuffer.IsFull)
        {
            m_videoCircularBuffer.Front().Sample.Dispose();
            m_videoCircularBuffer.PopFront();
        }

        var videoCache = new VideoCache(frame, pSample);
        m_videoCircularBuffer.PushBack(videoCache);
    }

    public void AddAudioSample(int startSample, Sample pSample)
    {
        int lastAudioSampleNum = LastAudioSampleNumber();
        if (lastAudioSampleNum != -1)
        {
            int actualAudioSampleNum = lastAudioSampleNum + m_audioCircularBuffer.Back().AudioSampleCount;
            if (Math.Abs(startSample - actualAudioSampleNum) > kAudioSampleWaringGapCount)
            {
                _logger.LogWarning(
                    "sample laggin - lag: {lag} startSample: {startSample} lastAudioSampleNum: {lastAudioSampleNum}",
                    startSample - actualAudioSampleNum,
                    startSample,
                    actualAudioSampleNum);
            }

            startSample = lastAudioSampleNum + m_audioCircularBuffer.Back().AudioSampleCount;
        }

        int totalLength = pSample.TotalLength;
        int audioSampleCount = totalLength / m_nBlockAlign;
        Debug.Assert((totalLength % m_nBlockAlign) == 0);

        if (m_audioCircularBuffer.IsFull)
        {
            m_audioCircularBuffer.Front().Sample.Dispose();
            m_audioCircularBuffer.PopFront();
        }

        var audioCache = new AudioCache(startSample, pSample, audioSampleCount);
        m_audioCircularBuffer.PushBack(audioCache);
    }

    public int LastFrameNumber()
    {
        if (m_videoCircularBuffer.Size > 0)
        {
            VideoCache prevVideoCache = m_videoCircularBuffer.Back();
            return prevVideoCache.Frame;
        }

        return -1;
    }

    public int LastAudioSampleNumber()
    {
        if (m_audioCircularBuffer.Size > 0)
        {
            AudioCache prevAudioCache = m_audioCircularBuffer.Back();
            return prevAudioCache.StartSampleNum;
        }

        return -1;
    }

    public Sample? SearchFrameSample(int frame)
    {
        foreach (VideoCache videoCache in m_videoCircularBuffer.Reverse())
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
        foreach (AudioCache audioCache in m_audioCircularBuffer)
        {
            if (audioCache.CopyBuffer(ref startSample, ref copySampleLength, ref buffer, m_nBlockAlign))
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
