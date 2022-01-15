using BeUtl.Media;
using BeUtl.Media.Pixel;
using BeUtl.Media.TextFormatting;

using NUnit.Framework;

using SkiaSharp;

namespace BeUtl.Graphics.UnitTests;

public class FormattedTextTests
{
    [SetUp]
    public void Setup()
    {
        _ = TypefaceProvider.Typeface();
    }

    [Test]
    public void Render()
    {
        Typeface face = TypefaceProvider.Typeface();
        FontFamily font = face.FontFamily;
        using var text = new FormattedText()
        {
            Lines =
            {
                new TextLine()
                {
                    Elements =
                    {
                        new TextElement
                        {
                            Text = "吾輩",
                            Foreground = Brushes.Black,
                            Font = font,
                            Size = 100,
                        },
                        new TextElement
                        {
                            Text = "は",
                            Foreground = Brushes.Black,
                            Font = font,
                            Size = 70,
                        },
                        new TextElement
                        {
                            Text = "猫",
                            Foreground = Brushes.Red,
                            Font = font,
                            Size = 100,
                        },
                        new TextElement
                        {
                            Text = "である",
                            Foreground = Brushes.Black,
                            Font = font,
                            Size = 70,
                        }
                    }
                },
                new TextLine()
                {
                    Elements =
                    {
                        new TextElement
                        {
                            Text = "名前",
                            Foreground = Brushes.Black,
                            Font = font,
                            Size = 100,
                        },
                        new TextElement
                        {
                            Text = "はまだ",
                            Foreground = Brushes.Black,
                            Font = font,
                            Size = 72,
                        },
                        new TextElement
                        {
                            Text = "無い",
                            Foreground = Brushes.Black,
                            Font = font,
                            Size = 100,
                        }
                    }
                }
            }
        };

        Size bounds = text.Bounds;
        using var graphics = new Canvas((int)bounds.Width, (int)bounds.Height);

        graphics.Clear(Colors.White);

        text.Draw(graphics);

        using Bitmap<Bgra8888> bmp = graphics.GetBitmap();

        Assert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), "1.png"), EncodedImageFormat.Png));
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
        using var text = FormattedText.Parse(str, new FormattedTextInfo(typeface, 100, Colors.Black, 0, default));

        Size bounds = text.Bounds;
        using var graphics = new Canvas((int)bounds.Width, (int)bounds.Height);

        graphics.Clear(Colors.White);

        text.Draw(graphics);

        using Bitmap<Bgra8888> bmp = graphics.GetBitmap();

        Assert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), "1.png"), EncodedImageFormat.Png));
    }
}
