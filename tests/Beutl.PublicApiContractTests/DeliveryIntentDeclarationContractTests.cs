using System.Reflection;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

/// <summary>
/// Pins that the surfaces whose intent decides fail-fast versus degrade make the caller say which one it
/// wants.
/// </summary>
/// <remarks>
/// A trailing optional <see cref="RenderIntent"/> defaulting to <see cref="RenderIntent.Preview"/> reads as a
/// convenience, but it silently rewrites a delivery host's failure policy: an intermediate that cannot be
/// allocated stops failing the render and starts dropping content, so an export ships a frame with a hole in
/// it and nothing in the source says why. The same applies to a brush host's materializer, without which a
/// <c>DrawableBrush</c> fill resolves to transparent.
/// </remarks>
[TestFixture]
public sealed class DeliveryIntentDeclarationContractTests
{
    [Test]
    public void TheRendererConstructor_RequiresAnExplicitIntent()
    {
        ParameterInfo intent = RequireParameter(typeof(Renderer), "intent");

        Assert.That(intent.HasDefaultValue, Is.False);
    }

    [TestCase("intent")]
    [TestCase("drawableBrushMaterializer")]
    public void TheBrushConstructor_RequiresAnExplicit(string parameterName)
    {
        ParameterInfo parameter = RequireParameter(typeof(BrushConstructor), parameterName);

        Assert.That(parameter.HasDefaultValue, Is.False);
    }

    [Test]
    public void ADeliveryRendererStillDeclaresItsIntent()
    {
        using var renderer = new Renderer(4, 4, RenderIntent.Delivery);

        Assert.That(renderer.Intent, Is.EqualTo(RenderIntent.Delivery));
    }

    [Test]
    public void ABrushConstructorWithoutAMaterializer_StillStatesIt()
    {
        var constructor = new BrushConstructor(
            new Rect(0, 0, 4, 4),
            Brushes.Resource.White,
            BlendMode.SrcOver,
            RenderIntent.Delivery,
            drawableBrushMaterializer: null);

        Assert.That(constructor.Intent, Is.EqualTo(RenderIntent.Delivery));
    }

    private static ParameterInfo RequireParameter(Type type, string name)
    {
        foreach (ConstructorInfo constructor in type.GetConstructors(
                     BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
            {
                if (parameter.Name == name)
                    return parameter;
            }
        }

        throw new InvalidOperationException($"No public {type.Name} constructor declares '{name}'.");
    }
}
