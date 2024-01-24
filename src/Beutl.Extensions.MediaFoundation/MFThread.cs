using Beutl.Threading;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

public static class MFThread
{
    public static Dispatcher Dispatcher { get; } = Dispatcher.Spawn();
}
