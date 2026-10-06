using Beutl.Language;

namespace Beutl.Controls.PropertyEditors;

internal static class ColorComponentHeaders
{
    public static void Apply(Vector3Editor editor, bool rgb)
    {
        if (rgb)
        {
            editor.FirstHeader = Strings.Red;
            editor.SecondHeader = Strings.Green;
            editor.ThirdHeader = Strings.Blue;
        }
        else
        {
            editor.FirstHeader = Strings.Hue;
            editor.SecondHeader = Strings.Saturation;
            editor.ThirdHeader = Strings.Brightness;
        }
    }
}
