using SharpDX.MediaFoundation;

#if MF_BUILD_IN
namespace Beutl.Embedding.MediaFoundation.Decoding;
#else
namespace Beutl.Extensions.MediaFoundation.Decoding;
#endif

public class VideoFormatName
{
    private static readonly Lazy<(Guid, string name)[]> s_videoFormats = new(() => typeof(VideoFormatGuids)
        .GetFields()
        .Select(f => f.GetValue(null) is Guid id ? ((Guid?)id, f) : (null, f))
        .Where(v => v.Item1.HasValue)
        .Select(v => (v.Item1!.Value, v.f.Name))
        .ToArray());

    public static string? GetName(Guid id)
    {
        return s_videoFormats.Value.FirstOrDefault(v => v.Item1 == id).name;
    }
}
