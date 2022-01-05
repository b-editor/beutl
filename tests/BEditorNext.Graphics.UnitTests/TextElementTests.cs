using BEditorNext.Media;
using BEditorNext.Media.TextFormatting;

using NUnit.Framework;

namespace BEditorNext.Graphics.UnitTests;

public class TextElementTests
{
    [Test]
    public void Measure()
    {
        var element = new TextElement
        {
            Foreground = Colors.White.ToBrush(),
            Size = 24,
            Text = "Text",
            Typeface = TypefaceProvider.Typeface()
        };

        _ = element.Measure();
    }
}
