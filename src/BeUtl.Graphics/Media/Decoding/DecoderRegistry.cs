namespace BeUtl.Media.Decoding;

public static class DecoderRegistry
{
    private static readonly List<IDecoderInfo> _registered = new();

    public static IEnumerable<IDecoderInfo> EnumerateDecoder()
    {
        return _registered;
    }

    public static MediaReader? OpenMediaFile(string file, MediaOptions options)
    {
        return GuessDecoder(file).FirstOrDefault()?.Open(file, options);
    }

    public static IDecoderInfo[] GuessDecoder(string file)
    {
        return _registered.Where(i => i.IsSupported(file)).ToArray();
    }

    public static void Register(IDecoderInfo decoder)
    {
        _registered.Add(decoder);
    }
}
