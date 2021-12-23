using BEditorNext.Media;
using BEditorNext.Media.Pixel;

namespace BEditorNext.Graphics.Effects;

public abstract unsafe class BitmapEffect
{
    public static Bitmap<Bgra8888> ApplyAll(Bitmap<Bgra8888> bitmap, IList<BitmapEffect> effects)
    {
        List<List<BitmapEffect>> summarized = Summarize(effects);

        for (int i = 0; i < summarized.Count; i++)
        {
            List<BitmapEffect> item = summarized[i];
            BitmapEffect first = item[0];

            if (first is PixelEffect)
            {
                Parallel.For(0, bitmap.Width * bitmap.Height, pos =>
                {
                    var ptr = (Bgra8888*)bitmap.Data;
                    for (int i = 0; i < item.Count; i++)
                    {
                        if (item[i] is PixelEffect pe)
                        {
                            pe.Apply(ref ptr[pos], bitmap.Info, pos);
                        }
                    }
                });
            }
            else if (first is RowEffect)
            {
                Parallel.For(0, bitmap.Height, pos =>
                {
                    Span<Bgra8888> span = bitmap.DataSpan[(pos * bitmap.Width)..];
                    for (int i = 0; i < item.Count; i++)
                    {
                        if (item[i] is RowEffect re)
                        {
                            re.Apply(span, bitmap.Info, pos);
                        }
                    }
                });
            }
            else
            {
                for (int ii = 0; ii < item.Count; ii++)
                {
                    item[ii].Apply(ref bitmap);
                }
            }
        }

        return bitmap;
    }

    public static List<List<BitmapEffect>> Summarize(IList<BitmapEffect> effects)
    {
        var list = new List<List<BitmapEffect>>();

        for (int i = 0; i < effects.Count; i++)
        {
            BitmapEffect effect = effects[i];

            if (effect is PixelEffect)
            {
                var inner = new List<BitmapEffect>();

                for (; i < effects.Count; i++)
                {
                    BitmapEffect effect2 = effects[i];

                    if (effect2 is PixelEffect)
                    {
                        inner.Add(effect2);
                    }
                    else
                    {
                        i--;
                        break;
                    }
                }

                list.Add(inner);
            }
            else if (effect is RowEffect)
            {
                var inner = new List<BitmapEffect>();

                for (; i < effects.Count; i++)
                {
                    BitmapEffect effect2 = effects[i];

                    if (effect2 is RowEffect)
                    {
                        inner.Add(effect2);
                    }
                    else
                    {
                        i--;
                        break;
                    }
                }

                list.Add(inner);
            }
            else
            {
                var inner = new List<BitmapEffect>();

                for (; i < effects.Count; i++)
                {
                    BitmapEffect effect2 = effects[i];

                    if (effect2 is not (PixelEffect or RowEffect))
                    {
                        inner.Add(effect2);
                    }
                    else
                    {
                        i--;
                        break;
                    }
                }

                list.Add(inner);
            }
        }

        return list;
    }

    public virtual PixelSize Measure(PixelSize size)
    {
        return size;
    }

    public abstract void Apply(ref Bitmap<Bgra8888> bitmap);
}

public abstract unsafe class PixelEffect : BitmapEffect
{
    public abstract void Apply(ref Bgra8888 pixel, in BitmapInfo info, int index);

    public override void Apply(ref Bitmap<Bgra8888> bitmap)
    {
        Bitmap<Bgra8888>? b = bitmap;

        Parallel.For(0, bitmap.Width * bitmap.Height, pos =>
        {
            var ptr = (Bgra8888*)b.Data;
            Apply(ref ptr[pos], b.Info, pos);
        });
    }
}

public abstract unsafe class RowEffect : BitmapEffect
{
    public abstract void Apply(Span<Bgra8888> pixel, in BitmapInfo info, int row);

    public override void Apply(ref Bitmap<Bgra8888> bitmap)
    {
        Bitmap<Bgra8888>? b = bitmap;

        Parallel.For(0, bitmap.Height, pos =>
        {
            Span<Bgra8888> span = b.DataSpan[(pos * b.Width)..];
            Apply(span, b.Info, pos);
        });
    }
}
