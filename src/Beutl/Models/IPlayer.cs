using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.Models;

internal interface IPlayer : IDisposable
{
    public record struct Frame(Ref<Bitmap> Bitmap, int Time);

    bool ProducerStopped { get; }

    void Start(PlaybackRange range);

    bool TryDequeue(out Frame frame);
}
