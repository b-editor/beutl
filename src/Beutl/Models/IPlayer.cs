using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.Models;

public interface IPlayer : IDisposable
{
    public record struct Frame(Ref<Bitmap> Bitmap, int Time);

    void Start();

    bool TryDequeue(out Frame frame);
}
