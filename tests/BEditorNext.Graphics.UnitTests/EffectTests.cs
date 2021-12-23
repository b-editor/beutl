using System.Diagnostics;

using BEditorNext.Graphics.Effects;
using BEditorNext.Media;
using BEditorNext.Media.Pixel;

using NUnit.Framework;

namespace BEditorNext.Graphics.UnitTests;

public class EffectTests
{
    [Test]
    public void BitmapEffectSummarize()
    {
        var result = BitmapEffect.Summarize(new List<BitmapEffect>()
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
        }
    }
}
