namespace Beutl.Utilities;

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
