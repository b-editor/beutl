namespace BEditor.Models
{
    public static class ConstantSettings
    {
        public static bool UseDarkMode { get; } = Settings.Default.UseDarkMode;

        public static double ClipHeight { get; } = Settings.Default.ClipHeight;

        public static float WidthOf1Frame { get; } = Settings.Default.WidthOf1Frame;
    }
}