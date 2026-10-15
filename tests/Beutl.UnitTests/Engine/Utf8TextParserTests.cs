using System.Text;
using Beutl.Graphics;
using Beutl.Graphics3D.Meshes;

namespace Beutl.UnitTests.Engine;

public class Utf8TextParserTests
{
    [Test]
    public void TryParse_Utf8_ReadsTheSameValueAsTheStringOverload()
    {
        const string text = "1,2,3; 4,5,6; 0.5,0.25; 1,0,0,-1";

        Assert.That(Vertex3D.TryParse(Encoding.UTF8.GetBytes(text), null, out Vertex3D vertex), Is.True);
        Assert.That(vertex, Is.EqualTo(Vertex3D.Parse(text)));
        Assert.That(Vertex3D.TryParse("1,2,3; 4,5,6"u8, null, out _), Is.False);
    }

    [Test]
    public void Parse_Utf8_LongerThanTheStackBuffer_UsesAPooledBuffer()
    {
        string padding = new(' ', 600);

        Assert.That(Point.Parse(Encoding.UTF8.GetBytes(padding + "1, 2" + padding)), Is.EqualTo(new Point(1, 2)));
    }
}
