using Beutl.Media;
using Beutl.Media.Source;

namespace Beutl.Services.AI;

internal static class AiResultImageLease
{
    public static Ref<Bitmap>? Acquire(Ref<Bitmap>? image)
        => image?.TryClone();
}
