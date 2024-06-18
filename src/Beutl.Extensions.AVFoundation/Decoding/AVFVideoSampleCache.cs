using Beutl.Collections;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using MonoMac.CoreMedia;

namespace Beutl.Extensions.AVFoundation.Decoding;

public class AVFVideoSampleCache(AVFSampleCacheOptions options)
{
    private readonly ILogger _logger = Log.CreateLogger<AVFVideoSampleCache>();

    public const int FrameWaringGapCount = 1;

    private CircularBuffer<VideoCache> _buffer = new(options.MaxVideoBufferSize);

    private readonly record struct VideoCache(int Frame, CMSampleBuffer Sample);

    public void Reset()
    {
        CircularBuffer<VideoCache> old = _buffer;
        _buffer = new CircularBuffer<VideoCache>(options.MaxVideoBufferSize);
        foreach (VideoCache item in old)
        {
            item.Sample.Dispose();
        }
    }

    public void Add(int frame, CMSampleBuffer pSample)
    {
        int lastFrameNum = LastFrameNumber();
        if (lastFrameNum != -1)
        {
            if (Math.Abs(lastFrameNum + 1 - frame) > FrameWaringGapCount)
            {
                _logger.LogWarning("frame error - frame: {frame} actual frame: {actual}", frame, lastFrameNum + 1);
            }

            frame = lastFrameNum + 1;
        }

        if (_buffer.IsFull)
        {
            _buffer.Front().Sample.Dispose();
            _buffer.PopFront();
        }

        var videoCache = new VideoCache(frame, pSample);
        _buffer.PushBack(videoCache);
    }

    public int LastFrameNumber()
    {
        if (_buffer.Size > 0)
        {
            VideoCache prevVideoCache = _buffer.Back();
            return prevVideoCache.Frame;
        }

        return -1;
    }

    public CMSampleBuffer? SearchSample(int frame)
    {
        foreach (VideoCache videoCache in _buffer.Reverse())
        {
            if (videoCache.Frame == frame)
            {
                return videoCache.Sample;
            }
        }

        return null;
    }
}
