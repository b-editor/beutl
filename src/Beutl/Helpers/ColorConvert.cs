namespace Beutl;

public static class ColorConvert
{
    public static Avalonia.Media.Color ToAvalonia(this in Media.Color color)
    {
        return Avalonia.Media.Color.FromArgb(color.A, color.R, color.G, color.B);
    }

    public static Media.Color ToMedia(this in Avalonia.Media.Color color)
    {
        return Media.Color.FromArgb(color.A, color.R, color.G, color.B);
    }
}
