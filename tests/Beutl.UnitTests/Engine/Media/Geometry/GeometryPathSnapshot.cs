using SkiaSharp;

namespace Beutl.UnitTests.Engine.Media.Geometry;

internal static class GeometryPathSnapshot
{
    public static IReadOnlyList<string> Describe(SKPath path)
    {
        var elements = new List<string>
        {
            $"fillType={path.FillType}",
            $"points={path.PointCount}",
            $"verbs={path.VerbCount}",
            $"tightBounds={path.TightBounds}",
            $"bounds={path.Bounds}",
            $"svg={path.ToSvgPathData()}",
        };

        using SKPath.RawIterator iterator = path.CreateRawIterator();
        Span<SKPoint> points = stackalloc SKPoint[4];
        SKPathVerb verb;
        int index = 0;
        do
        {
            verb = iterator.Next(points);
            elements.Add(verb switch
            {
                SKPathVerb.Move => $"[{index}] Move {points[0]}",
                SKPathVerb.Line => $"[{index}] Line {points[0]} {points[1]}",
                SKPathVerb.Quad => $"[{index}] Quad {points[0]} {points[1]} {points[2]}",
                SKPathVerb.Conic =>
                    $"[{index}] Conic {points[0]} {points[1]} {points[2]} w={iterator.ConicWeight()}",
                SKPathVerb.Cubic => $"[{index}] Cubic {points[0]} {points[1]} {points[2]} {points[3]}",
                SKPathVerb.Close => $"[{index}] Close",
                _ => $"[{index}] Done",
            });
            index++;
        } while (verb != SKPathVerb.Done);

        return elements;
    }
}
