﻿using Beutl.Media.Music;

namespace Beutl.Media.Encoding;

[Obsolete("Use EncodingController instead.")]
public abstract class MediaWriter : IDisposable
{
    protected MediaWriter(VideoEncoderSettings videoConfig, AudioEncoderSettings audioConfig)
    {
        VideoConfig = videoConfig;
        AudioConfig = audioConfig;
        MemoryManagement.TrackDisposable(this, DisposableCategory.IO);
    }

    ~MediaWriter()
    {
        Dispose(disposing: false);
    }

    public bool IsDisposed { get; private set; }

    public VideoEncoderSettings VideoConfig { get; }

    public AudioEncoderSettings AudioConfig { get; }

    public abstract long NumberOfFrames { get; }

    public abstract long NumberOfSamples { get; }

    public static MediaWriter CreateMediaWriter(
        string file,
        VideoEncoderSettings? videoConfig = null,
        AudioEncoderSettings? audioConfig = null,
        IEncoderInfo? encoder = null)
    {
        encoder ??= EncoderRegistry.GuessEncoder(file).FirstOrDefault();
        if (encoder == null)
            throw new Exception("Encoder not found.");

        videoConfig ??= encoder.DefaultVideoConfig();
        audioConfig ??= encoder.DefaultAudioConfig();

        return encoder.Create(file, videoConfig, audioConfig)
            ?? throw new NotSupportedException("Not supported format.");
    }

    public abstract bool AddVideo(IBitmap image);

    public abstract bool AddAudio(IPcm sound);

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
