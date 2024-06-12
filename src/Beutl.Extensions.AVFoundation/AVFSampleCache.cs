using Beutl.Collections;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using MonoMac.CoreMedia;

namespace Beutl.Extensions.AVFoundation;

public class AVFSampleCache(AVFSampleCacheOptions options)
{
    private readonly ILogger _logger = Log.CreateLogger<AVFSampleCache>();

    public const int FrameWaringGapCount = 1;

    private CircularBuffer<VideoCache> _videoCircularBuffer = new(options.MaxVideoBufferSize);

    private readonly record struct VideoCache(int Frame, CMSampleBuffer Sample);

    public void ResetVideo()
    {
        CircularBuffer<VideoCache> old = _videoCircularBuffer;
        _videoCircularBuffer = new CircularBuffer<VideoCache>(options.MaxVideoBufferSize);
        foreach (VideoCache item in old)
        {
            item.Sample.Dispose();
        }
    }

    public void AddFrameSample(int frame, CMSampleBuffer pSample)
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

    public int LastFrameNumber()
    {
        if (_videoCircularBuffer.Size > 0)
        {
            VideoCache prevVideoCache = _videoCircularBuffer.Back();
            return prevVideoCache.Frame;
        }

        return -1;
    }

    public CMSampleBuffer? SearchFrameSample(int frame)
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
}
