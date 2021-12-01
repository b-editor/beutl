using BEditorNext.Graphics.Pixel;

using NUnit.Framework;

using SkiaSharp;

namespace BEditorNext.Graphics.UnitTests;

public class FormattedTextTests
{
    [Test]
    public void Render()
    {
        using SKTypeface face = TypefaceProvider.CreateTypeface();
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
                            Color = Colors.Black,
                            Font = face,
                            Size = 100,
                        },
                        new TextElement
                        {
                            Text = "は",
                            Color = Colors.Black,
                            Font = face,
                            Size = 70,
                        },
                        new TextElement
                        {
                            Text = "猫",
                            Color = Colors.Red,
                            Font = face,
                            Size = 100,
                        },
                        new TextElement
                        {
                            Text = "である",
                            Color = Colors.Black,
                            Font = face,
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
                            Color = Colors.Black,
                            Font = face,
                            Size = 100,
                        },
                        new TextElement
                        {
                            Text = "はまだ",
                            Color = Colors.Black,
                            Font = face,
                            Size = 72,
                        },
                        new TextElement
                        {
                            Text = "無い",
                            Color = Colors.Black,
                            Font = face,
                            Size = 100,
                        }
                    }
                }
            }
        };

        Size bounds = text.Bounds;
        using var graphics = new Graphics((int)bounds.Width, (int)bounds.Height);

        graphics.Clear(Colors.White);

        text.Render(graphics);

        using Bitmap<Bgra8888> bmp = graphics.GetBitmap();

        Assert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), "1.png"), EncodedImageFormat.Png));
    }

    [Test]
    public void Parse()
    {
        string str = @"
吾輩<size=70>は</size><#ff0000>猫</#><size=70>である。</size>
名前<size=70>はまだ</size>無<size=70>い。";
        SKTypeface typeface = TypefaceProvider.CreateTypeface();
        using var text = FormattedText.Parse(str, new FormattedTextInfo(typeface, 100, Colors.Black, 0, default));

        Size bounds = text.Bounds;
        using var graphics = new Graphics((int)bounds.Width, (int)bounds.Height);

        graphics.Clear(Colors.White);

        text.Render(graphics);

        using Bitmap<Bgra8888> bmp = graphics.GetBitmap();

        Assert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), "1.png"), EncodedImageFormat.Png));
    }
}
