using System.Text;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class FiniteCurrentPixelEffectTests
{
    [Test]
    public void Gamma_ShaderBoundsPowerAndHalfConversion()
    {
        string source = RecordSource(new Gamma());

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("corrected = min("));
            Assert.That(source, Does.Contain("clamp(result * alpha"));
        });
    }

    [Test]
    public void ColorGrading_ShaderBoundsPowerBeforeLaterColorMath()
    {
        string source = RecordSource(new ColorGrading());

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("color = min("));
            Assert.That(source, Does.Contain("clamp(color * gn"));
            Assert.That(source, Does.Contain("clamp(rgb * alpha"));
        });
    }

    [TestCase(CubeFileDimension.OneDimension)]
    [TestCase(CubeFileDimension.ThreeDimension)]
    public void LutEffect_ShaderBoundsTransferFunctions(CubeFileDimension dimension)
    {
        var effect = new LutEffect
        {
            Source = { CurrentValue = CreateLutSource(dimension) },
        };

        string source = RecordSource(effect);

        Assert.Multiple(() =>
        {
            Assert.That(source, Does.Contain("pow(max(c, float3(0.0))"));
            Assert.That(source, Does.Contain("pow(max((c + 0.055) / 1.055, float3(0.0))"));
            Assert.That(source, Does.Contain("clamp(result * alpha"));
            if (dimension == CubeFileDimension.ThreeDimension)
                Assert.That(source, Does.Contain("float3 boundedColor = clamp(inputColor"));
        });
    }

    private static string RecordSource(FilterEffect effect)
    {
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(new Rect(0, 0, 1, 1));
        context.ApplyTransactional(effect, resource);
        var item = (FEItem_Shader)context.GetOrderedItems().Single();
        return item.Description.Source.Text;
    }

    private static CubeSource CreateLutSource(CubeFileDimension dimension)
    {
        string header = dimension == CubeFileDimension.OneDimension
            ? "LUT_1D_SIZE 2"
            : "LUT_3D_SIZE 2";
        int entries = dimension == CubeFileDimension.OneDimension ? 2 : 8;
        string cubeText = $"{header}\nDOMAIN_MIN 0 0 0\nDOMAIN_MAX 1 1 1\n"
                          + string.Concat(Enumerable.Repeat("0 0 0\n", entries));
        var source = new CubeSource();
        source.ReadFrom(new Uri(
            "data:text/plain;base64,"
            + Convert.ToBase64String(Encoding.ASCII.GetBytes(cubeText))));
        return source;
    }
}
