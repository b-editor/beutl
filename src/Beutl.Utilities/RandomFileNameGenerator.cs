using System.Diagnostics.CodeAnalysis;

namespace Beutl.Utilities;

[ExcludeFromCodeCoverage]
public static class RandomFileNameGenerator
{
    public static string Generate(string baseDir, string ext)
    {
        string filename = Path.Combine(baseDir, $"{RandomString()}.{ext}");
        while (File.Exists(filename))
        {
            filename = Path.Combine(baseDir, $"{RandomString()}.{ext}");
        }

        return filename;
    }

    public static Uri GenerateUri(string baseDir, string ext)
    {
        return new Uri(new Uri("file://"), Generate(baseDir, ext));
    }

    public static Uri GenerateUri(Uri baseDir, string ext)
    {
        return new Uri(new Uri("file://"), Generate(Path.GetDirectoryName(baseDir.LocalPath)!, ext));
    }

    private static string RandomString()
    {
        const string characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        Span<char> Charsarr = stackalloc char[8];
        var random = new Random();

        for (int i = 0; i < Charsarr.Length; i++)
        {
            Charsarr[i] = characters[random.Next(characters.Length)];
        }

        return new string(Charsarr);
    }
}
