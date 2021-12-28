using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using BEditorNext.Graphics.Effects;

using BEditorNext.Media;

namespace BEditorNext.Graphics;

public interface IDrawable : IDisposable
{
    PixelSize Size { get; }

    ref Matrix3x2 Transform { get; }

    AlignmentX HorizontalAlignment { get; set; }

    AlignmentY VerticalAlignment { get; set; }
    
    AlignmentX HorizontalContentAlignment { get; set; }

    AlignmentY VerticalContentAlignment { get; set; }

    IList<BitmapEffect> Effects { get; }

    void Draw(ICanvas canvas);
}
