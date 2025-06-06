﻿using System.Diagnostics.CodeAnalysis;
using Beutl.Media.Music;

namespace Beutl.Media.Decoding;

public abstract class MediaReader : IDisposable
{
    protected MediaReader()
    {
        MemoryManagement.TrackDisposable(this, DisposableCategory.IO);
    }

    ~MediaReader()
    {
        Dispose(disposing: false);
    }

    public bool IsDisposed { get; private set; }

    public abstract VideoStreamInfo VideoInfo { get; }

    public abstract AudioStreamInfo AudioInfo { get; }

    public abstract bool HasVideo { get; }

    public abstract bool HasAudio { get; }

    public static MediaReader Open(string file)
    {
        return Open(file, new MediaOptions());
    }

    public static MediaReader Open(string file, MediaOptions options)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(options);
        if (!File.Exists(file))
        {
            throw new FileNotFoundException(null, file);
        }

        return DecoderRegistry.OpenMediaFile(file, options) ?? throw new Exception();
    }

    public abstract bool ReadVideo(int frame, [NotNullWhen(true)] out IBitmap? image);

    public abstract bool ReadAudio(int start, int length, [NotNullWhen(true)] out IPcm? sound);

    public void Dispose()
    {
        if (!IsDisposed)
        {
            Dispose(disposing: true);
            MemoryManagement.MarkDisposed(this);
            GC.SuppressFinalize(this);

            IsDisposed = true;
        }
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}
