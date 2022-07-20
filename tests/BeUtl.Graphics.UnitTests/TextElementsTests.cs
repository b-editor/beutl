
using System.Text;

using BeUtl.Graphics.Shapes;
using BeUtl.Media;

using NUnit.Framework;

using FormattedTextInfo = BeUtl.Media.TextFormatting.FormattedTextInfo;
using FormattedTextParser = BeUtl.Media.TextFormatting.FormattedTextParser;
using FormattedTextTokenizer = BeUtl.Media.TextFormatting.FormattedTextTokenizer;
using FormattedText_ = BeUtl.Media.TextFormatting.FormattedText_;
using System.Runtime.InteropServices;

namespace BeUtl.Graphics.UnitTests;

public class TextElementsTests
{
    [SetUp]
    public void Setup()
    {
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
        var tokenizer = new FormattedTextTokenizer(str)
        {
            ExperimentalVersion = true
        };
        tokenizer.Tokenize();

        var options = new FormattedTextInfo(typeface, 100, Colors.Black, 0, default);

        var elements = FormattedTextParser.ToElements(options, CollectionsMarshal.AsSpan(tokenizer.Result));

        var tb = new TextBlock
        {
            Elements = new TextElements(elements)
        };

        var sb = new StringBuilder(str.Length);
        var texts = new List<FormattedText_>();
        Console.WriteLine("Start enumerate lines.");
        foreach (Span<FormattedText_> span in tb.Elements.Lines)
        {
            foreach (FormattedText_ item in span)
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
        var items = new TextElements(new TextElement_[]
        {
            new TextElement_()
            {
                Size = 72,
                Text = "ABC\nDEF"
            },
            new TextElement_()
            {
                Size = 64,
                Text = "GHI\n\rJKL\r"
            },
            new TextElement_()
            {
                Size = 56,
                Text = "\nMNO"
            }
        });

        foreach (var item in items.Lines)
        {

        }
    }
}
