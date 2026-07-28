using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using Beutl.Utilities;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

public abstract partial class DisplacementMapTransform : EngineObject
{
    internal abstract void ApplyTo(
        Brush.Resource displacementMap, Resource resource, GradientSpreadMethod spreadMethod,
        DisplacementMapChannel channel, bool signed, FilterEffectContext context);

    protected static RenderResource<Brush.Resource> BorrowDisplacementMap(
        FilterEffectContext context,
        Brush.Resource displacementMap)
        => context.Borrow(
            displacementMap,
            BrushRecorder.GetResourceIdentity(displacementMap),
            displacementMap.Version);

    protected static void AddDisplacementBindings(
        ShaderBindingBuilder bindings,
        RenderResource<Brush.Resource> displacementMap,
        DisplacementMapChannel channel,
        bool signed)
    {
        bindings.Resource(
            "uDisplacementMap",
            displacementMap,
            ShaderResourceCoordinateSpace.OutputDevice,
            CreateDisplacementMapShader,
            structuralKey: typeof(DisplacementMapTransform),
            runtimeIdentity: new RenderRuntimeIdentity("DisplacementMapTransform.map"));
        bindings.Uniform("uChannel", (int)channel);
        bindings.Uniform("uSigned", signed ? 1 : 0);
    }

    protected static void BindScaledVector(
        ShaderUniformWriter writer,
        Vector2 value,
        ShaderExecutionContext context)
        => writer.Set(value * context.WorkingScale);

    protected static void BindPivot(
        ShaderUniformWriter writer,
        Vector2 center,
        ShaderExecutionContext context)
    {
        var semanticOrigin = context.OutputBounds.Position - context.LogicalOrigin;
        writer.Set(new Vector2(
            (semanticOrigin.X + context.OutputBounds.Width / 2 + center.X) * context.WorkingScale,
            (semanticOrigin.Y + context.OutputBounds.Height / 2 + center.Y) * context.WorkingScale));
    }

    private static void CreateDisplacementMapShader(
        ShaderResourceWriter writer,
        Brush.Resource displacementMap,
        ShaderExecutionContext context)
    {
        SKShader? shader = new BrushConstructor(
                new Rect(context.OutputBounds.Size),
                displacementMap,
                BlendMode.SrcOver,
                context.WorkingScale,
                context.MaxWorkingScale)
            .CreateShader();
        if (shader is null)
        {
            writer.Set(SKShader.CreateColor(SKColors.Transparent));
            return;
        }

        SKShader? mapped = null;
        try
        {
            var semanticOrigin = context.OutputBounds.Position - context.LogicalOrigin;
            SKMatrix localMatrix = SKMatrix.CreateScaleTranslation(
                context.WorkingScale,
                context.WorkingScale,
                semanticOrigin.X * context.WorkingScale,
                semanticOrigin.Y * context.WorkingScale);
            if (localMatrix.IsIdentity)
            {
                writer.Set(shader);
                shader = null;
                return;
            }

            mapped = shader.WithLocalMatrix(localMatrix);
            if (mapped is null)
            {
                writer.Set(shader);
                shader = null;
            }
            else
            {
                writer.Set(mapped);
                mapped = null;
            }
        }
        finally
        {
            mapped?.Dispose();
            shader?.Dispose();
        }
    }
}

[Display(Name = nameof(GraphicsStrings.TranslateTransform), ResourceType = typeof(GraphicsStrings))]
public partial class DisplacementMapTranslateTransform : DisplacementMapTransform
{
    private const string ShaderSource =
        """
        uniform shader src;
        uniform shader uDisplacementMap;

        uniform float2 uTranslation;
        uniform int uChannel;
        uniform int uSigned;

        float getDisplacement(half4 dispColor) {
            float d;
            if (uChannel == 0) d = dispColor.a;
            else {
                if (uChannel == 1) d = dot(dispColor.rgb, half3(0.2126, 0.7152, 0.0722));
                else if (uChannel == 2) d = dispColor.r;
                else if (uChannel == 3) d = dispColor.g;
                else d = dispColor.b;
                d = d * dispColor.a;
            }
            if (uSigned != 0) d = d * 2.0 - 1.0;
            return d;
        }

        half4 main(float2 coord) {
            half4 dispColor = uDisplacementMap.eval(coord);
            float2 offset = uTranslation * getDisplacement(dispColor);

            float2 uv = coord + offset;
            return src.eval(uv);
        }
        """;

    public DisplacementMapTranslateTransform()
    {
        ScanProperties<DisplacementMapTranslateTransform>();
    }

    [Display(Name = nameof(GraphicsStrings.TranslateTransform_X), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> X { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.TranslateTransform_Y), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Y { get; } = Property.CreateAnimatable<float>();

    internal override void ApplyTo(
        Brush.Resource displacementMap, DisplacementMapTransform.Resource resource,
        GradientSpreadMethod spreadMethod, DisplacementMapChannel channel, bool signed, FilterEffectContext context)
    {
        var r = (Resource)resource;
        RenderResource<Brush.Resource> map = BorrowDisplacementMap(context, displacementMap);
        context.Shader(ShaderDescription.WholeSource(
            ShaderSource,
            RenderBoundsContract.FullInput,
            bindings =>
            {
                AddDisplacementBindings(bindings, map, channel, signed);
                bindings.Uniform(
                    "uTranslation",
                    new Vector2(r.X, r.Y),
                    BindScaledVector,
                    structuralKey: typeof(DisplacementMapTranslateTransform),
                    runtimeIdentity: new RenderRuntimeIdentity("DisplacementMapTranslateTransform.translation"));
            },
            spreadMethod.ToSKShaderTileMode()));
    }
}

[Display(Name = nameof(GraphicsStrings.Scale), ResourceType = typeof(GraphicsStrings))]
public partial class DisplacementMapScaleTransform : DisplacementMapTransform
{
    private const string ShaderSource =
        """
        uniform shader src;
        uniform shader uDisplacementMap;

        uniform float2 uScale;
        uniform float2 uPivot;
        uniform int uChannel;
        uniform int uSigned;

        float getDisplacement(half4 dispColor) {
            float d;
            if (uChannel == 0) d = dispColor.a;
            else {
                if (uChannel == 1) d = dot(dispColor.rgb, half3(0.2126, 0.7152, 0.0722));
                else if (uChannel == 2) d = dispColor.r;
                else if (uChannel == 3) d = dispColor.g;
                else d = dispColor.b;
                d = d * dispColor.a;
            }
            if (uSigned != 0) d = d * 2.0 - 1.0;
            return d;
        }

        half4 main(float2 coord) {
            half4 dispColor = uDisplacementMap.eval(coord);
            float2 s = max(mix(float2(1.0, 1.0), uScale, getDisplacement(dispColor)), float2(0.001, 0.001));

            float2 uv = (coord - uPivot) / s + uPivot;
            return src.eval(uv);
        }
        """;

    public DisplacementMapScaleTransform()
    {
        ScanProperties<DisplacementMapScaleTransform>();
    }

    [Display(Name = nameof(GraphicsStrings.Scale), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Scale { get; } = Property.CreateAnimatable<float>(100);

    [Display(Name = nameof(GraphicsStrings.ScaleTransform_ScaleX), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> ScaleX { get; } = Property.CreateAnimatable<float>(100);

    [Display(Name = nameof(GraphicsStrings.ScaleTransform_ScaleY), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> ScaleY { get; } = Property.CreateAnimatable<float>(100);

    [Display(Name = nameof(GraphicsStrings.CenterX), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> CenterX { get; } = Property.CreateAnimatable<float>();

    [Display(Name = nameof(GraphicsStrings.CenterY), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> CenterY { get; } = Property.CreateAnimatable<float>();

    internal override void ApplyTo(
        Brush.Resource displacementMap, DisplacementMapTransform.Resource resource,
        GradientSpreadMethod spreadMethod, DisplacementMapChannel channel, bool signed, FilterEffectContext context)
    {
        var r = (Resource)resource;
        RenderResource<Brush.Resource> map = BorrowDisplacementMap(context, displacementMap);
        context.Shader(ShaderDescription.WholeSource(
            ShaderSource,
            RenderBoundsContract.FullInput,
            bindings =>
            {
                AddDisplacementBindings(bindings, map, channel, signed);
                bindings.Uniform(
                    "uScale",
                    new Vector2(
                        r.Scale * r.ScaleX / 10000,
                        r.Scale * r.ScaleY / 10000));
                bindings.Uniform(
                    "uPivot",
                    new Vector2(r.CenterX, r.CenterY),
                    BindPivot,
                    structuralKey: typeof(DisplacementMapScaleTransform),
                    runtimeIdentity: new RenderRuntimeIdentity("DisplacementMapScaleTransform.pivot"));
            },
            spreadMethod.ToSKShaderTileMode()));
    }
}

[Display(Name = nameof(GraphicsStrings.Rotation), ResourceType = typeof(GraphicsStrings))]
public partial class DisplacementMapRotationTransform : DisplacementMapTransform
{
    private const string ShaderSource =
        """
        uniform shader src;
        uniform shader uDisplacementMap;

        uniform float uAngle;
        uniform float2 uPivot;
        uniform int uChannel;
        uniform int uSigned;

        float getDisplacement(half4 dispColor) {
            float d;
            if (uChannel == 0) d = dispColor.a;
            else {
                if (uChannel == 1) d = dot(dispColor.rgb, half3(0.2126, 0.7152, 0.0722));
                else if (uChannel == 2) d = dispColor.r;
                else if (uChannel == 3) d = dispColor.g;
                else d = dispColor.b;
                d = d * dispColor.a;
            }
            if (uSigned != 0) d = d * 2.0 - 1.0;
            return d;
        }

        half4 main(float2 coord) {
            half4 dispColor = uDisplacementMap.eval(coord);
            float disp = getDisplacement(dispColor);
            float2 offset = float2(cos(uAngle * disp), sin(uAngle * disp));

            float2 uv = coord - uPivot;
            float2 rotated = float2(uv.x * offset.x - uv.y * offset.y, uv.x * offset.y + uv.y * offset.x);
            uv = rotated + uPivot;
            return src.eval(uv);
        }
        """;

    public DisplacementMapRotationTransform()
    {
        ScanProperties<DisplacementMapRotationTransform>();
    }

    [Display(Name = nameof(GraphicsStrings.Rotation), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Rotation { get; } = Property.CreateAnimatable<float>(0);

    [Display(Name = nameof(GraphicsStrings.CenterX), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> CenterX { get; } = Property.CreateAnimatable<float>(0);

    [Display(Name = nameof(GraphicsStrings.CenterY), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> CenterY { get; } = Property.CreateAnimatable<float>(0);

    internal override void ApplyTo(
        Brush.Resource displacementMap, DisplacementMapTransform.Resource resource,
        GradientSpreadMethod spreadMethod, DisplacementMapChannel channel, bool signed, FilterEffectContext context)
    {
        var r = (Resource)resource;
        RenderResource<Brush.Resource> map = BorrowDisplacementMap(context, displacementMap);
        context.Shader(ShaderDescription.WholeSource(
            ShaderSource,
            RenderBoundsContract.FullInput,
            bindings =>
            {
                AddDisplacementBindings(bindings, map, channel, signed);
                bindings.Uniform("uAngle", MathUtilities.Deg2Rad(r.Rotation));
                bindings.Uniform(
                    "uPivot",
                    new Vector2(r.CenterX, r.CenterY),
                    BindPivot,
                    structuralKey: typeof(DisplacementMapRotationTransform),
                    runtimeIdentity: new RenderRuntimeIdentity("DisplacementMapRotationTransform.pivot"));
            },
            spreadMethod.ToSKShaderTileMode()));
    }
}
