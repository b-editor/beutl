using Beutl.Graphics.Shapes;
using Beutl.Logging;
using Beutl.Media.TextFormatting;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Engine;

public class TextElementsTests
{
    [SetUp]
    public void Setup()
    {
        Log.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
        _ = TypefaceProvider.Typeface();
    }

    [Test]
    public void LinesEnumerator()
    {
        var items = new TextElements(
        [
            new TextElement()
            {
                Size = 72,
                Text = "ABC\nDEF"
            },
            new TextElement()
            {
                Size = 64,
                Text = "GHI\n\rJKL\r"
            },
            new TextElement()
            {
                Size = 56,
                Text = "\nMNO"
            }
        ]);

        foreach (Span<FormattedText> _ in items.Lines)
        {

        }
    }
}
