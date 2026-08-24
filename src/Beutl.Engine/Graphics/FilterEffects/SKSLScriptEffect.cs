using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

using SkiaSharp;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.SKSLScriptEffect), ResourceType = typeof(GraphicsStrings))]
public sealed partial class SKSLScriptEffect : FilterEffect, IScriptCompilableEffect
{
    private static readonly ILogger s_logger = Log.CreateLogger<SKSLScriptEffect>();

    public SKSLScriptEffect()
    {
        ScanProperties<SKSLScriptEffect>();
    }

    [Display(Name = nameof(GraphicsStrings.Script), ResourceType = typeof(GraphicsStrings))]
    [DataType(DataType.MultilineText)]
    public IProperty<string> Script { get; } = Property.Create(GetDefaultScript());

    private static string GetDefaultScript()
    {
        return """
               uniform shader src;
               uniform float progress;  // 0.0 - 1.0
               uniform float duration;  // seconds
               uniform float time;      // seconds
               uniform float width;     // render target width (device px)
               uniform float height;    // render target height (device px)
               // Also available:
               // uniform float2 iResolution;  // (width, height) in device px
               // uniform float  iScale;       // working density (device px per logical px)
               // uniform float  iTime;

               half4 main(float2 fragCoord) {
                   half4 c = src.eval(fragCoord);
                   return c;
               }
               """;
    }

    public ScriptCompilationResult ValidateScript(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return ScriptCompilationResult.Compiled;

        string? declarativeError = null;
        if (SkslSource.HasCurrentPixelEntryPoint(script))
        {
            try
            {
                var source = new SkslSource(script, ShaderDescriptionKind.CurrentPixel);
                ValidateDeclarativeUniforms(source, ShaderDescriptionKind.CurrentPixel);
                using SKRuntimeEffect? currentPixelEffect = SKRuntimeEffect.CreateShader(
                    CreateCurrentPixelProgram(source),
                    out declarativeError);
                if (currentPixelEffect is not null && string.IsNullOrEmpty(declarativeError))
                    return ScriptCompilationResult.Compiled;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                declarativeError = ex.Message;
            }
        }

        try
        {
            using var effect = SKRuntimeEffect.CreateShader(script, out string? errorText);
            if (effect is not null && string.IsNullOrEmpty(errorText))
                return ScriptCompilationResult.Compiled;

            return string.IsNullOrEmpty(declarativeError)
                ? ScriptCompilationResult.Fail(errorText ?? "Failed to compile SKSL script.")
                : ScriptCompilationResult.Fail(declarativeError);
        }
        catch (Exception ex)
        {
            return ScriptCompilationResult.Fail(declarativeError ?? ex.Message);
        }
    }

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;

        if (r._definition is not null)
        {
            context.Shader(r._definition.Call(new Resource.ScriptUniformState(
                r.Progress,
                r.Duration,
                r.Time)));
            return;
        }

        if (r._shader is not null)
        {
            context.CustomEffect(
                (Resource: r.Progress, duration: r.Duration, time: r.Time, shader: r._shader,
                    compileError: r._compileError),
                OnApplyTo,
                static (_, r) => r);
        }
    }

    private static void OnApplyTo(
        (float progress, float duration, float time, SKSLShader shader, string? compileError) data,
        CustomFilterEffectContext c)
    {
        for (int i = 0; i < c.Targets.Count; i++)
        {
            EffectTarget effectTarget = c.Targets[i];
            EffectTarget output = c.CreateTargetLike(effectTarget);
            try
            {
                RenderTarget? outputRenderTarget = output.RenderTarget;
                if (outputRenderTarget is null || output.Scale.IsUnbounded)
                {
                    output.Dispose();
                    continue;
                }

                using SKSLShaderBuilder builder = data.shader.CreateBuilder();

                if (builder.Uniforms.Contains("progress"))
                    builder.Uniforms["progress"] = data.progress;
                if (builder.Uniforms.Contains("duration"))
                    builder.Uniforms["duration"] = data.duration;
                if (builder.Uniforms.Contains("time"))
                    builder.Uniforms["time"] = data.time;

                float w = output.Scale.Value;
                int deviceWidth = outputRenderTarget.Width;
                int deviceHeight = outputRenderTarget.Height;
                if (builder.Uniforms.Contains("width"))
                    builder.Uniforms["width"] = (float)deviceWidth;
                if (builder.Uniforms.Contains("height"))
                    builder.Uniforms["height"] = (float)deviceHeight;
                if (builder.Uniforms.Contains("iResolution"))
                    builder.Uniforms["iResolution"] = new SKPoint(deviceWidth, deviceHeight);
                if (builder.Uniforms.Contains("iScale"))
                    builder.Uniforms["iScale"] = w;
                if (builder.Uniforms.Contains("iTime"))
                    builder.Uniforms["iTime"] = data.time;

                if (builder.Children.Contains("src"))
                {
                    bool rendered = c.UseMappedInputShader(
                        effectTarget,
                        output,
                        (Builder: builder, Shader: data.shader, Context: c, Output: output),
                        static (state, mappedSource) =>
                        {
                            state.Builder.Children["src"] = mappedSource;
                            state.Shader.RenderToTarget(state.Context, state.Builder, state.Output);
                        },
                        SKShaderTileMode.Clamp,
                        SKShaderTileMode.Clamp);
                    if (!rendered)
                    {
                        output.Dispose();
                        continue;
                    }
                }
                else
                {
                    data.shader.RenderToTarget(c, builder, output);
                }

                effectTarget.Dispose();
                c.Targets[i] = output;
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }
    }

    public new partial class Resource
    {
        internal SKSLShader? _shader;
        internal ShaderDefinition<ScriptUniformState>? _definition;
        internal string? _compiledScript;
        internal string? _compileError;

        public float Progress { get; private set; }

        public float Duration { get; private set; }

        public float Time { get; private set; }

        partial void PostUpdate(SKSLScriptEffect obj, CompositionContext context)
        {
            float duration = (float)obj.TimeRange.Duration.TotalSeconds;
            float time = (float)(context.Time - obj.TimeRange.Start).TotalSeconds;
            float progress = duration > 0 ? time / duration : 0;

            // ReSharper disable CompareOfFloatsByEqualityOperator
            if (Duration != duration || Time != time || Progress != progress)
            {
                Version++;
            }
            // ReSharper restore CompareOfFloatsByEqualityOperator

            Duration = duration;
            Time = time;
            Progress = progress;
            CompileScript(Script);
        }

        private void CompileScript(string script)
        {
            if (_compiledScript == script)
                return;

            _shader?.Dispose();
            _shader = null;
            _definition = null;
            var prevError = _compileError;
            _compileError = null;
            _compiledScript = script;

            if (string.IsNullOrWhiteSpace(script))
                return;

            string? declarativeError = null;
            bool hasCurrentPixelEntryPoint = SkslSource.HasCurrentPixelEntryPoint(script);
            if (hasCurrentPixelEntryPoint)
            {
                try
                {
                    ShaderDefinition<ScriptUniformState> definition = CreateCurrentPixelDefinition(script);
                    ShaderDescription description = definition.Call(default).Description;
                    if (TryCompileProgram(CreateCurrentPixelProgram(description.Source), out declarativeError))
                    {
                        _definition = definition;
                        return;
                    }
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    declarativeError = ex.Message;
                }
            }
            else
            {
                try
                {
                    ShaderDefinition<ScriptUniformState> definition = CreateWholeSourceDefinition(script);
                    ShaderDescription description = definition.Call(default).Description;
                    if (TryCompileProgram(description.Source.Text, out declarativeError))
                    {
                        _definition = definition;
                        return;
                    }
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    declarativeError = ex.Message;
                }
            }

            if (!SKSLShader.TryCreate(script, out _shader, out string? effectItemError))
            {
                SetCompileError(declarativeError ?? effectItemError, prevError);
                return;
            }

            // A valid effect-item program can remain here only when the stricter declarative source or binding contract
            // could not represent it.
        }

        private static ShaderDefinition<ScriptUniformState> CreateCurrentPixelDefinition(string script)
        {
            var source = new SkslSource(script, ShaderDescriptionKind.CurrentPixel);
            ValidateDeclarativeUniforms(source, ShaderDescriptionKind.CurrentPixel);
            return ShaderDefinition<ScriptUniformState>.CurrentPixel(
                source,
                builder => BindUniforms(builder, source, isWholeSource: false));
        }

        private static ShaderDefinition<ScriptUniformState> CreateWholeSourceDefinition(string script)
        {
            string declarativeSource = SkslSource.HasUniformDeclaration(script, "src")
                ? script
                : "uniform shader src;\n" + script;
            var source = new SkslSource(declarativeSource, ShaderDescriptionKind.WholeSource);

            ValidateDeclarativeUniforms(source, ShaderDescriptionKind.WholeSource);
            return ShaderDefinition<ScriptUniformState>.WholeSource(
                source,
                RenderBoundsContract.FullInput,
                builder => BindUniforms(builder, source, isWholeSource: true),
                SKShaderTileMode.Clamp);
        }

        private static void BindUniforms(
            ShaderDefinitionBuilder<ScriptUniformState> builder,
            SkslSource source,
            bool isWholeSource)
        {
            foreach ((string name, SkslUniformDeclaration declaration) in source.Uniforms)
            {
                if (isWholeSource && name == "src" && declaration.IsShader)
                    continue;

                switch (name)
                {
                    case "progress":
                        builder.Uniform(name, static state => state.Progress);
                        break;
                    case "duration":
                        builder.Uniform(name, static state => state.Duration);
                        break;
                    case "time":
                    case "iTime":
                        builder.Uniform(name, static state => state.Time);
                        break;
                    case "width":
                        builder.Uniform(name, static _ => 0f, BindWidth);
                        break;
                    case "height":
                        builder.Uniform(name, static _ => 0f, BindHeight);
                        break;
                    case "iResolution":
                        builder.Uniform(name, static _ => default(Vector2), BindResolution);
                        break;
                    case "iScale":
                        builder.Uniform(name, static _ => 0f, BindScale);
                        break;
                    default:
                        BindZero(builder, name, declaration);
                        break;
                }
            }
        }

        private static void BindWidth(ShaderUniformWriter writer, float _, ShaderExecutionContext context)
            => writer.Set((float)context.SemanticOutputSize.Width);

        private static void BindHeight(ShaderUniformWriter writer, float _, ShaderExecutionContext context)
            => writer.Set((float)context.SemanticOutputSize.Height);

        private static void BindResolution(ShaderUniformWriter writer, Vector2 _, ShaderExecutionContext context)
            => writer.Set(new Vector2(context.SemanticOutputSize.Width, context.SemanticOutputSize.Height));

        private static void BindScale(ShaderUniformWriter writer, float _, ShaderExecutionContext context)
            => writer.Set(context.WorkingScale);

        private void SetCompileError(string? error, string? previousError)
        {
            _compileError = error ?? "Failed to compile SKSL script.";
            if (previousError != _compileError)
                s_logger.LogError("Failed to compile SKSL script: {Error}", _compileError);
        }

        partial void PostDispose(bool disposing)
        {
            _shader?.Dispose();
            _shader = null;
            _definition = null;
            _compileError = null;
        }

        private static void BindZero(
            ShaderDefinitionBuilder<ScriptUniformState> builder,
            string name,
            SkslUniformDeclaration declaration)
        {
            (ZeroBindingKind kind, int componentCount) = GetZeroBindingKind(declaration);
            switch (kind)
            {
                case ZeroBindingKind.FloatingPoint:
                    builder.ConstantUniform(name, new float[componentCount]);
                    break;
                case ZeroBindingKind.Integer:
                    builder.ConstantUniform(name, 0);
                    break;
                case ZeroBindingKind.Boolean:
                    builder.ConstantUniform(name, false);
                    break;
                default:
                    throw new InvalidOperationException("The zero-binding kind is invalid.");
            }
        }

        internal readonly record struct ScriptUniformState(float Progress, float Duration, float Time);
    }

    private static void ValidateDeclarativeUniforms(SkslSource source, ShaderDescriptionKind kind)
    {
        foreach ((string name, SkslUniformDeclaration declaration) in source.Uniforms)
        {
            if (kind == ShaderDescriptionKind.WholeSource && name == "src" && declaration.IsShader)
                continue;

            switch (name)
            {
                case "progress":
                case "duration":
                case "time":
                case "width":
                case "height":
                case "iScale":
                case "iTime":
                    if (declaration.ArrayExtent is not null || declaration.Type is not ("float" or "half"))
                        throw new InvalidOperationException($"Uniform '{name}' must be a floating-point scalar.");
                    break;
                case "iResolution":
                    if (declaration.ArrayExtent is not null || declaration.Type is not ("float2" or "half2"))
                        throw new InvalidOperationException("Uniform 'iResolution' must be a floating-point vector2.");
                    break;
                default:
                    _ = GetZeroBindingKind(declaration);
                    break;
            }
        }
    }

    private static (ZeroBindingKind Kind, int ComponentCount) GetZeroBindingKind(
        SkslUniformDeclaration declaration)
    {
        int componentCount = declaration.Type switch
        {
            "float" or "half" => 1,
            "float2" or "half2" => 2,
            "float3" or "half3" => 3,
            "float4" or "half4" => 4,
            "float2x2" or "half2x2" or "mat2" => 4,
            "float3x3" or "half3x3" or "mat3" => 9,
            "float4x4" or "half4x4" or "mat4" => 16,
            _ => 0,
        };
        if (componentCount > 0)
        {
            return (
                ZeroBindingKind.FloatingPoint,
                checked(componentCount * (declaration.ArrayExtent ?? 1)));
        }

        if (declaration.ArrayExtent is null && declaration.Type == "int")
            return (ZeroBindingKind.Integer, 1);
        if (declaration.ArrayExtent is null && declaration.Type == "bool")
            return (ZeroBindingKind.Boolean, 1);

        throw new InvalidOperationException(
            $"Uniform type '{declaration.Type}' does not have a canonical declarative zero value.");
    }

    private static bool TryCompileProgram(string source, out string? errorText)
    {
        if (!SKSLShader.TryCreate(source, out SKSLShader? shader, out errorText))
            return false;

        shader!.Dispose();
        return true;
    }

    private static string CreateCurrentPixelProgram(SkslSource source)
        => $"uniform shader __beutl_src;\n{source.Text}\n"
           + "half4 main(float2 __beutl_coord) { return apply(__beutl_src.eval(__beutl_coord)); }\n";

    private enum ZeroBindingKind : byte
    {
        FloatingPoint,
        Integer,
        Boolean,
    }
}
