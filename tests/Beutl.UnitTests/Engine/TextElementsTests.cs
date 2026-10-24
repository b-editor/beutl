using Beutl.Graphics.Shapes;
using Beutl.Media.TextFormatting;

namespace Beutl.UnitTests.Engine;

public class TextElementsTests
{
    [SetUp]
    public void Setup()
    {
        _ = TypefaceProvider.Typeface();
    }

    // A line feed or a lone carriage return breaks the line, so "\n\r" and "\r" then "\n" each leave an empty line.
    [Test]
    public void LinesEnumerator()
    {
        using var items = new TextElements(
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

        for (int enumeration = 0; enumeration < 2; enumeration++)
        {
            var lines = new List<string[]>();
            foreach (Span<FormattedText> line in items.Lines)
            {
                string[] texts = new string[line.Length];
                for (int i = 0; i < line.Length; i++)
                    texts[i] = line[i].Text.ToString();
                lines.Add(texts);
            }

            Assert.That(lines, Is.EqualTo(new[]
            {
                new[] { "ABC" },
                new[] { "DEF", "GHI" },
                new[] { "" },
                new[] { "JKL" },
                new[] { "" },
                new[] { "MNO" },
            }));

            items[0].Text = "Changed\nmore\nlines";
        }
    }
}
