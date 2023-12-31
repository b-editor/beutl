using Beutl.Configuration;

namespace Beutl.Media.Decoding;

public static class DecoderRegistry
{
    private static readonly List<IDecoderInfo> s_registered = [];
    private static readonly List<IDecoderInfo> s_ordered = [];
    private static readonly object s_lock = new();

    static DecoderRegistry()
    {
        GlobalConfiguration.Instance.ExtensionConfig.DecoderPriority.CollectionChanged += (_, _) => InvalidateCache();
    }

    private static void InvalidateCache()
    {
        lock (s_lock)
        {
            s_ordered.Clear();
        }
    }

    public static IEnumerable<IDecoderInfo> EnumerateDecoder()
    {
        lock (s_lock)
        {
            if (s_ordered.Count == 0)
            {
                ExtensionConfig extensionConfig = GlobalConfiguration.Instance.ExtensionConfig;
                IDecoderInfo[] preferred = extensionConfig.DecoderPriority.Select(v => v.Type)
                    .Where(v => v != null)
                    .Select(t => s_registered.FirstOrDefault(v => v.GetType() == t))
                    .Where(v => v != null)
                    .ToArray()!;

                s_ordered.AddRange(preferred);

                s_ordered.AddRange(s_registered.Except(preferred));
            }

            return s_ordered;
        }
    }

    public static MediaReader? OpenMediaFile(string file, MediaOptions options)
    {
        foreach (IDecoderInfo decoder in GuessDecoder(file))
        {
            if (decoder.Open(file, options) is { } reader)
            {
                return reader;
            }
        }

        return null;
    }

    public static IDecoderInfo[] GuessDecoder(string file)
    {
        return EnumerateDecoder().Where(i => i.IsSupported(file)).ToArray();
    }

    public static void Register(IDecoderInfo decoder)
    {
        lock (s_lock)
        {
            InvalidateCache();
            s_registered.Add(decoder);
        }
    }
}
