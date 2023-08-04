namespace Beutl.Media.Source;

public interface IMediaSource : IDisposable
{
    bool IsDisposed { get; }

    string Name { get; }

    IMediaSource Clone();
}
