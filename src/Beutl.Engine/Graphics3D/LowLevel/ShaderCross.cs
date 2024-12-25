using Beutl.Logging;
using Microsoft.Extensions.Logging;
using SDL;
using TrippyGL;

namespace Beutl.Graphics3D;

public class ShaderCross
{
    private static readonly ILogger s_logger = Log.CreateLogger<ShaderCross>();

    public enum ShaderFormat
    {
        Invalid,
        SPIRV,
        HLSL
    }

    public readonly record struct HLSLDefine(string Name, string Value);

    public static Graphics3D.ShaderFormat SPIRVDestinationFormats => SDL_ShaderCross.SDL_ShaderCross_GetSPIRVShaderFormats();

    public static Graphics3D.ShaderFormat HLSLDestinationFormats => SDL_ShaderCross.SDL_ShaderCross_GetHLSLShaderFormats();

    public static bool Initialized { get; private set; }

    public static bool Initialize()
    {
        if (SDL_ShaderCross.SDL_ShaderCross_Init() <= 0)
        {
            s_logger.LogError("Failed to initialize ShaderCross: {Error}", SDL3.SDL_GetError());
            return false;
        }

        Initialized = true;
        return true;
    }

    public static Shader Create(
        Device device,
        string filepath,
        string entrypoint,
        ShaderFormat shaderFormat,
        ShaderStage shaderStage,
        bool enableDebug = false,
        string? name = null,
        string? includeDir = null, // Only used by HLSL
        params Span<HLSLDefine> defines // Only used by HLSL
    )
    {
        using var stream = new FileStream(filepath, FileMode.Open, FileAccess.Read);
        name ??= Path.GetFileName(filepath); // if name is not provided, just use the filename
        return Create(
            device,
            stream,
            entrypoint,
            shaderFormat,
            shaderStage,
            enableDebug,
            name,
            includeDir,
            defines);
    }

    public static Shader Create(
        Device device,
        Stream stream,
        string entrypoint,
        ShaderFormat shaderFormat,
        ShaderStage shaderStage,
        bool enableDebug = false,
        string? name = null,
        string? includeDir = null, /* Only used by HLSL */
        params Span<HLSLDefine> defines /* Only used by HLSL */)
    {
        switch (shaderFormat)
        {
            case ShaderFormat.SPIRV:
                return Shader.CreateFromSPIRV(device, stream, entrypoint, shaderStage, enableDebug, name);
            case ShaderFormat.HLSL:
                return Shader.CreateFromHLSL(device, stream, entrypoint, includeDir, shaderStage, enableDebug, name, defines);
            case ShaderFormat.Invalid:
            default:
                throw new InvalidOperationException("Invalid shader format!");
        }
    }

    public static ComputePipeline Create(
        GraphicsDevice device,
        string filepath,
        string entrypoint,
        ShaderFormat shaderFormat,
        bool enableDebug = false,
        string? name = null,
        string? includeDir = null, /* Only used by HLSL */
        params Span<HLSLDefine> defines /* Only used by HLSL */)
    {
        using var stream = new FileStream(filepath, FileMode.Open, FileAccess.Read);
        name ??= Path.GetFileName(filepath); // if name not provided, just use filename
        return Create(
            device,
            stream,
            entrypoint,
            shaderFormat,
            enableDebug,
            name,
            includeDir,
            defines);
    }

    public static ComputePipeline Create(
        GraphicsDevice device,
        Stream stream,
        string entrypoint,
        ShaderFormat shaderFormat,
        bool enableDebug = false,
        string? name = null,
        string? includeDir = null, /* Only used by HLSL */
        params Span<HLSLDefine> defines /* Only used by HLSL */)
    {
        if (shaderFormat == ShaderFormat.SPIRV)
        {
            return ComputePipeline.CreateFromSPIRV(
                device,
                stream,
                entrypoint,
                enableDebug,
                name);
        }
        else if (shaderFormat == ShaderFormat.HLSL)
        {
            return ComputePipeline.CreateFromHLSL(
                device,
                stream,
                entrypoint,
                includeDir,
                enableDebug,
                name,
                defines);
        }
        else
        {
            throw new InvalidOperationException("Invalid shader format!");
        }
    }

    public static void Quit()
    {
        if (Initialized)
        {
            SDL_ShaderCross.SDL_ShaderCross_Quit();
        }

        Initialized = false;
    }
}
