namespace Beutl.Media.Encoding;

public static class EncoderRegistry
{
    private static readonly List<IEncoderInfo> s_registerd = [];

    public static IEnumerable<IEncoderInfo> EnumerateEncoders()
    {
        return s_registerd;
    }

    public static IEncoderInfo[] GuessEncoder(string file)
    {
        return s_registerd.Where(i => i.IsSupported(file)).ToArray();
    }

    public static void Register(IEncoderInfo metadata)
    {
        s_registerd.Add(metadata);
    }
}
