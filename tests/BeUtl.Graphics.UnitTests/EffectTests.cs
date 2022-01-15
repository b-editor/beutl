using System.Diagnostics;

using BeUtl.Graphics.Effects;
using BeUtl.Media;
using BeUtl.Media.Pixel;

using NUnit.Framework;

namespace BeUtl.Graphics.UnitTests;

public class EffectTests
{
    [Test]
    public void BitmapEffectApplyFor()
    {
        var bmp = new Bitmap<Bgra8888>(1000, 1000);

        var list = new List<BitmapEffect>()
        {
            new BitmapEffectImpl(1),
            new BitmapEffectImpl(2),
            new PixelEffectImpl(3),
            new PixelEffectImpl(4),
            new PixelEffectImpl(5),
            new RowEffectImpl(6),
            new RowEffectImpl(7),
            new PixelEffectImpl(8),
        };

        for (int i = 0; i < list.Count; i++)
        {
            BitmapEffect effect = list[i];
            effect.Apply(ref bmp);
        }

        bmp.Dispose();
    }

    [Test]
    public void BitmapEffectApplyAll()
    {
        using var bmp = new Bitmap<Bgra8888>(1000, 1000);

        Bitmap<Bgra8888> result = BitmapEffect.ApplyAll(bmp, new List<BitmapEffect>()
        {
            new BitmapEffectImpl(1),
            new BitmapEffectImpl(2),
            new PixelEffectImpl(3),
            new PixelEffectImpl(4),
            new PixelEffectImpl(5),
            new RowEffectImpl(6),
            new RowEffectImpl(7),
            new PixelEffectImpl(8),
        });

        result.Dispose();
    }

    [DebuggerDisplay("{Id}")]
    public class BitmapEffectImpl : BitmapEffect
    {
        public BitmapEffectImpl(int id)
        {
            Id = id;
        }

        public int Id { get; }

        public override void Apply(ref Bitmap<Bgra8888> bitmap)
        {
        }
    }

    [DebuggerDisplay("{Id}")]
    public class PixelEffectImpl : PixelEffect
    {
        public PixelEffectImpl(int id)
        {
            Id = id;
        }

        public int Id { get; }

        public override void Apply(ref Bgra8888 pixel, in BitmapInfo info, int index)
        {
            pixel.A = 255;
            pixel.B = (byte)(pixel.B ^ 128);
            pixel.G = (byte)(pixel.G ^ 128);
            pixel.R = (byte)(pixel.R ^ 128);
        }
    }

    [DebuggerDisplay("{Id}")]
    public class RowEffectImpl : RowEffect
    {
        public RowEffectImpl(int id)
        {
            Id = id;
        }

        public int Id { get; }

        public override void Apply(Span<Bgra8888> pixel, in BitmapInfo info, int row)
        {
            pixel.Reverse();
        }
    }
}
