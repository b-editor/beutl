
using System.Runtime.InteropServices;
using System.Text;

using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.TextFormatting;

using Microsoft.Extensions.Logging;

using NUnit.Framework;

namespace Beutl.Graphics.UnitTests;

public class TextElementsTests
{
    [SetUp]
    public void Setup()
    {
        BeutlApplication.Current.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
        _ = TypefaceProvider.Typeface();
    }

    [Test]
    public void Parse()
    {
        string str = @"
<b>吾輩</b><size=70>は</size><#ff0000>猫</#><size=70>である。</size>
<i>名前</i><size=70>はまだ</size>無<size=70>い。</cspace>

<font='Roboto'>Roboto</font>
<noparse><font='Noto Sans JP'><bold>Noto Sans</font></bold></noparse>
";
        Typeface typeface = TypefaceProvider.Typeface();
        var tokenizer = new FormattedTextTokenizer(str);
        tokenizer.Tokenize();

        var options = new FormattedTextInfo(typeface, 100, Colors.Black, 0, null);
        var builder = new TextElementsBuilder(options);
        builder.AppendTokens(CollectionsMarshal.AsSpan(tokenizer.Result));

        var tb = new TextBlock
        {
            Elements = new TextElements(builder.Items.ToArray())
        };

        var sb = new StringBuilder(str.Length);
        var texts = new List<FormattedText>();
        Console.WriteLine("Start enumerate lines.");
        foreach (Span<FormattedText> span in tb.Elements.Lines)
        {
            foreach (FormattedText item in span)
            {
                texts.Add(item);
                if (item.BeginOnNewLine)
                {
                    sb.AppendLine();
                    sb.Append(item.Text.AsSpan());
                }
                else
                {
                    sb.Append(item.Text.AsSpan());
                }
            }
        }

        Console.Write(sb.ToString());

        //text.Measure(Size.Infinity);
        //Rect bounds = text.Bounds;
        //using var graphics = new Canvas((int)bounds.Width, (int)bounds.Height);

        //graphics.Clear(Colors.White);

        //text.Draw(graphics);

        //using Bitmap<Bgra8888> bmp = graphics.GetBitmap();

        //Assert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), "1.png"), EncodedImageFormat.Png));
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
