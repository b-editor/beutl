using Beutl.Media.Decoding;

namespace Beutl.Views.Editors;

internal static class DecoderFileExtensions
{
    public static string[] GetFilePatterns(Func<IDecoderInfo, IEnumerable<string>> selector)
    {
        return DecoderRegistry.EnumerateDecoder()
            .SelectMany(selector)
            .Distinct()
            .Select(NormalizePattern)
            .ToArray();
    }

    private static string NormalizePattern(string extension)
    {
        if (extension.Contains('*', StringComparison.Ordinal))
        {
            return extension;
        }

        return extension.StartsWith('.') ? $"*{extension}" : $"*.{extension}";
    }
}
