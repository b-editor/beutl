using System.Globalization;
using System.Text.RegularExpressions;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

// The SDR preview's brush and the HDR present shader draw the checkerboard from the same grays (#4066).
public class PreviewCheckerboardTests
{
    [Test]
    public void TheShaderTakesTheSameGrays()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ShaderConstant("checkerLight"), Is.EqualTo(PreviewCheckerboard.Light));
            Assert.That(ShaderConstant("checkerDark"), Is.EqualTo(PreviewCheckerboard.Dark));
        });
    }

    private static float ShaderConstant(string name)
    {
        Match match = Regex.Match(PreviewCheckerboard.ShaderSource, $@"const float {name} = ([0-9.]+);");
        Assert.That(match.Success, Is.True, $"{name} is in the shader source");
        return float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }
}
