using NUnit.Framework;

namespace BEditorNext.Graphics.UnitTests;

public class TextElementTests
{
    [Test]
    public void Measure()
    {
        var element = new TextElement
        {
            Color = Colors.White,
            Size = 24,
            Text = "Text",
            Font = TypefaceProvider.CreateTypeface()
        };

        _ = element.Measure();

        element.Font.Dispose();
    }
}
