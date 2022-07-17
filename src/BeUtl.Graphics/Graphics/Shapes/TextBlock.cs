using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BeUtl.Media;

namespace BeUtl.Graphics.Shapes;

public class TextBlock : Drawable
{
    public static readonly CoreProperty<FontWeight> FontWeightProperty;
    public static readonly CoreProperty<FontStyle> FontStyleProperty;
    public static readonly CoreProperty<FontFamily> FontFamilyProperty;
    public static readonly CoreProperty<float> SizeProperty;
    public static readonly CoreProperty<float> SpacingProperty;
    public static readonly CoreProperty<string> TextProperty;
    public static readonly CoreProperty<Thickness> MarginProperty;
    public static readonly CoreProperty<TextElements> ElementsProperty;

    public FontWeight Weight { get; set; }

    public FontStyle Style { get; set; }

    public FontFamily Font { get; set; }
    
    public float Size { get; set; }

    public float Spacing { get; set; }

    public string Text { get; set; }

    public Thickness Margin { get; set; }

    public TextElements Elements { get; set; }

    protected override Size MeasureCore(Size availableSize) => throw new NotImplementedException();

    protected override void OnDraw(ICanvas canvas) => throw new NotImplementedException();


}
