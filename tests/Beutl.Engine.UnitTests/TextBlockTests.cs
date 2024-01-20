
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Logging;

using Microsoft.Extensions.Logging;

using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace Beutl.Graphics.UnitTests;

public class TextBlockTests
{
    private const string Case1 = @"<b>吾輩</b><size=70>は</size><#ff0000>猫</#><size=70>である。</size>
<i>名前</i><size=70>はまだ</size>無<size=70>い。</cspace>

<single-line>
<b>この文字列は</b>
複数行に分かれて
ます。
</single-line>

<stroke='Gray,5,flat,miter,outside,2'>縁取り</stroke>
<font='Roboto'>Roboto</font>
<noparse><font='Noto Sans JP'><bold>Noto Sans</font></bold></noparse>";
    private const string Case2 = "吾輩は猫である";

    [SetUp]
    public void Setup()
    {
        Log.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
        _ = TypefaceProvider.Typeface();
    }

    [Test]
    [TestCase(Case1, 0)]
    [TestCase(Case2, 1)]
    public void ParseAndDraw(string str, int id)
    {
        Typeface typeface = TypefaceProvider.Typeface();
        var tb = new TextBlock()
        {
            FontFamily = typeface.FontFamily,
            FontStyle = typeface.Style,
            FontWeight = typeface.Weight,
            Size = 100,
            Fill = Brushes.Black,
            Spacing = 0,
            Text = str
        };

        tb.Measure(Size.Infinity);
        Rect bounds = tb.Bounds;
        using var graphics = new ImmediateCanvas((int)bounds.Width, (int)bounds.Height);

        graphics.Clear(Colors.White);

        tb.Render(graphics);

        using Bitmap<Bgra8888> bmp = graphics.GetBitmap();

        ClassicAssert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"{id}.png"), EncodedImageFormat.Png));
    }

    [Test]
    public void ToSKPath()
    {
        Typeface typeface = TypefaceProvider.Typeface();
        var tb = new TextBlock()
        {
            FontFamily = typeface.FontFamily,
            FontStyle = typeface.Style,
            FontWeight = typeface.Weight,
            Size = 100,
            Fill = Brushes.White,
            Spacing = 0,
            Text = Case1
        };

        tb.Measure(Size.Infinity);
        Rect bounds = tb.Bounds;
        using var skpath = TextBlock.ToSKPath(tb.Elements!);

        using var graphics = new ImmediateCanvas((int)bounds.Width, (int)bounds.Height);

        graphics.Clear(Colors.White);

        var pen = new Pen
        {
            Brush = Brushes.Black,
            Thickness = 5,
            StrokeAlignment = StrokeAlignment.Outside
        };
        graphics.DrawSKPath(skpath, false, null, pen);

        using Bitmap<Bgra8888> bmp = graphics.GetBitmap();

        ClassicAssert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"0.png"), EncodedImageFormat.Png));
    }
}
