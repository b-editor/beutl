using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using Beutl.UnitTests.TestInfrastructure;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

/// <summary>Records filter effects, runs them on solid targets and binds what their shader stages declare.</summary>
internal static class FilterEffectTestHarness
{
    /// <summary>Records <paramref name="effect"/> over <paramref name="bounds"/>, expecting one shader stage.</summary>
    /// <returns>The stage and the bounds the recording reported.</returns>
    public static (ShaderDescription Description, Rect Bounds) RecordShader(FilterEffect effect, Rect bounds)
    {
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(bounds);
        context.ApplyTransactional(resource);

        IReadOnlyList<IFEItem> items = context.GetOrderedItems();
        Assert.Multiple(() =>
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items.OfType<IFEItem_Custom>(), Is.Empty);
        });
        return (((FEItem_Shader)items.Single()).Description, context.Bounds);
    }

    /// <summary>
    /// Fills a CPU target of <paramref name="size"/> with <paramref name="color"/>, runs <paramref name="effect"/>
    /// on it, and reads every channel before and after.
    /// </summary>
    public static (float[] Before, float[] After) RunOnSolidTarget(FilterEffect effect, SKColor color, PixelSize size)
    {
        var bounds = new Rect(0, 0, size.Width, size.Height);
        using RenderTarget backing = CpuRenderTargets.Create(size.Width, size.Height);
        backing.Value.Canvas.Clear(color);
        backing.Value.Canvas.Flush();
        using Bitmap beforeBitmap = backing.Snapshot();
        float[] before = ReadChannels(beforeBitmap);
        using var targets = new EffectTargets
        {
            new EffectTarget(
                backing,
                bounds,
                EffectiveScale.At(1),
                new PixelRect(0, 0, size.Width, size.Height)),
        };
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(bounds);
        context.ApplyTransactional(resource);
        using var builder = new SKImageFilterBuilder();
        using var executor = new FilterEffectExecutor(
            targets,
            builder,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            drawableBrushMaterializer: null,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1);

        executor.Apply(context);
        executor.Flush(false);

        using Bitmap afterBitmap = targets.Single().RenderTarget!.Snapshot();
        return (before, ReadChannels(afterBitmap));
    }

    /// <summary>An effect target over <paramref name="bounds"/> cleared to red.</summary>
    public static EffectTarget CreateSolidTarget(Rect bounds)
    {
        using RenderTarget renderTarget = RenderTarget.Create((int)bounds.Width, (int)bounds.Height)
            ?? throw new InvalidOperationException("A CPU render target is required for this test.");
        using (var canvas = new ImmediateCanvas(
                   renderTarget,
                   RenderIntent.Preview,
                   density: 1,
                   maxWorkingScale: 1,
                   logicalSize: bounds.Size))
        {
            canvas.Clear(Colors.Red);
        }

        return new EffectTarget(renderTarget, bounds, EffectiveScale.At(1));
    }

    /// <summary>
    /// Binds the uniform <paramref name="name"/> for an execution whose regions are all <paramref name="bounds"/>.
    /// </summary>
    public static ShaderUniformValue BindUniform(ShaderDescription description, string name, Rect bounds)
    {
        ShaderUniformBinding binding = description.Uniforms.Single(item => item.Name == name);
        SkslUniformDeclaration declaration = description.Source.Uniforms[name];
        var token = new RenderExecutionSessionToken();
        try
        {
            return binding.Bind(declaration, CreateExecution(token, bounds));
        }
        finally
        {
            token.Complete();
        }
    }

    /// <summary>Binds and releases the one resource of <paramref name="description"/>.</summary>
    public static void BindResource(ShaderDescription description, Rect bounds)
    {
        var token = new RenderExecutionSessionToken();
        try
        {
            description.Resources.Single().Bind(CreateExecution(token, bounds)).Dispose();
        }
        finally
        {
            token.Complete();
        }
    }

    private static float[] ReadChannels(Bitmap bitmap)
        => bitmap.GetPixelSpan<ushort>()
            .ToArray()
            .Select(static bits => (float)BitConverter.UInt16BitsToHalf(bits))
            .ToArray();

    private static ShaderExecutionContext CreateExecution(RenderExecutionSessionToken token, Rect bounds)
        => new(
            token,
            bounds,
            bounds,
            bounds,
            PixelRect.FromRect(bounds, 1),
            bounds.Position,
            EffectiveScale.At(1),
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1,
            intent: RenderIntent.Preview,
            purpose: RenderRequestPurpose.Frame);
}
