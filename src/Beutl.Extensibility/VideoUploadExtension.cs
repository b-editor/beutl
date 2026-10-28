using Avalonia.Controls;
using Reactive.Bindings;

namespace Beutl.Extensibility;

/// <summary>Provides authentication, metadata editing and uploading for a video service.</summary>
public abstract class VideoUploadExtension : Extension
{
    /// <summary>Creates independent settings for one output profile, on the UI thread.</summary>
    public abstract IVideoUploadContext CreateContext();

    /// <summary>Creates the authentication and metadata controls for that context.</summary>
    public abstract Control CreateControl(IVideoUploadContext context);
}

/// <summary>Editable upload settings. Serialize metadata only, never account credentials or tokens.</summary>
public interface IVideoUploadContext : IDisposable, IJsonSerializable
{
    /// <summary>Whether authentication and required metadata are ready for an upload.</summary>
    IReadOnlyReactiveProperty<bool> IsReady { get; }

    /// <summary>Validates and snapshots the current settings without starting network I/O.</summary>
    /// <remarks>
    /// Called on the UI thread before encoding, or when a user requests a later upload. The file may
    /// not exist yet. The returned operation must be independent of subsequent edits or disposal of
    /// this context. The host starts it only after the output file has been successfully committed.
    /// </remarks>
    IVideoUploadOperation CreateUpload(string filePath);
}

/// <summary>A single upload using captured settings and authentication.</summary>
public interface IVideoUploadOperation : IDisposable
{
    /// <summary>Uploads the file and returns its remote URL, if the service supplies one.</summary>
    /// <remarks>
    /// Progress is a fraction between zero and one. Await all I/O and cleanup before returning.
    /// Never delete or modify the local video, including after failure or cancellation. Throw on
    /// failure; returning means the service acknowledged the upload, not necessarily that its
    /// processing has finished or the video is public. Cancellation may leave a remote draft.
    /// </remarks>
    Task<Uri?> UploadAsync(IProgress<double> progress, CancellationToken cancellationToken);
}
