using Beutl.IO;

namespace Beutl.Media.Source;

public interface IMediaSource : IDisposable, IFileSource
{
    bool IsDisposed { get; }

    [Obsolete("Use Uri property.")]
    string Name => Uri.LocalPath;

    IMediaSource Clone();
}
