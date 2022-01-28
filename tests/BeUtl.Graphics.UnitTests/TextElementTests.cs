using BeUtl.Media;
using BeUtl.Media.TextFormatting;

using NUnit.Framework;

namespace BeUtl.Graphics.UnitTests;

public class TextElementTests
{
    [Test]
    public void Measure()
    {
        var element = new TextElement
        {
            Foreground = Brushes.White,
            Size = 24,
            Text = "Text",
            Typeface = TypefaceProvider.Typeface()
        };

        element.Measure(new Size(500, 500));
    }
}
