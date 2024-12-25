using System.Runtime.InteropServices;
using System.Text;
using SDL;

namespace Beutl.Graphics3D;

public unsafe class Shader : GraphicsResource
{
    private readonly ShaderCreateInfo _shaderCreateInfo;

    private Shader(Device device, SDL_GPUShader* handle, in ShaderCreateInfo shaderCreateInfo) : base(device)
    {
        _shaderCreateInfo = shaderCreateInfo;
        Handle = handle;
    }

    public uint NumSamplers => _shaderCreateInfo.NumSamplers;

    public uint NumStorageTextures => _shaderCreateInfo.NumStorageTextures;

    public uint NumStorageBuffers => _shaderCreateInfo.NumStorageBuffers;

    public uint NumUniformBuffers => _shaderCreateInfo.NumUniformBuffers;

    internal SDL_GPUShader* Handle { get; }

    public static Shader Create(
        Device device, string filePath,
        string entryPoint, in ShaderCreateInfo shaderCreateInfo)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        return Create(
            device, stream,
            entryPoint, shaderCreateInfo);
    }

    public static Shader Create(
        Device device, Stream stream,
        string entryPoint, in ShaderCreateInfo shaderCreateInfo)
    {
        var bytecodeBuffer = NativeMemory.Alloc((nuint)stream.Length);
        var bytecodeSpan = new Span<byte>(bytecodeBuffer, (int)stream.Length);
        stream.ReadExactly(bytecodeSpan);

        byte* entryPointBuffer = MarshalString(entryPoint);

        var createInfo = shaderCreateInfo.ToNative();
        createInfo.code_size = (nuint)stream.Length;
        createInfo.code = (byte*)bytecodeBuffer;
        createInfo.entrypoint = entryPointBuffer;

        var shaderModule = SDL3.SDL_CreateGPUShader(device.Handle, &createInfo);

        NativeMemory.Free(bytecodeBuffer);
        NativeMemory.Free(entryPointBuffer);

        if (shaderModule == null)
        {
            throw new InvalidOperationException($"Failed to compile shader: {SDL3.SDL_GetError()}");
        }

        return new Shader(device, shaderModule, shaderCreateInfo);
    }

    internal static Shader CreateFromSPIRV(
        Device device, Stream stream,
        string entryPoint, ShaderStage shaderStage,
        bool enableDebug, string? name)
    {
        var bytecodeBuffer = NativeMemory.Alloc((nuint)stream.Length);
        var bytecodeSpan = new Span<byte>(bytecodeBuffer, (int)stream.Length);
        stream.ReadExactly(bytecodeSpan);

        byte* entryPointBuffer = MarshalString(entryPoint);
        byte* nameBuffer = MarshalString(name);

        SDL_ShaderCross.INTERNAL_SPIRVInfo spirvInfo;
        spirvInfo.Bytecode = (byte*)bytecodeBuffer;
        spirvInfo.BytecodeSize = (nuint)stream.Length;
        spirvInfo.EntryPoint = entryPointBuffer;
        spirvInfo.ShaderStage = (SDL_ShaderCross.ShaderStage)shaderStage;
        spirvInfo.EnableDebug = enableDebug;
        spirvInfo.Name = nameBuffer;
        spirvInfo.Props = 0;

        SDL_ShaderCross.GraphicsShaderMetadata shaderMetadata;

        var shaderModule = SDL_ShaderCross.SDL_ShaderCross_CompileGraphicsShaderFromSPIRV(
            device.Handle, &spirvInfo, &shaderMetadata);

        NativeMemory.Free(bytecodeBuffer);
        NativeMemory.Free(entryPointBuffer);
        NativeMemory.Free(nameBuffer);

        if (shaderModule == null)
        {
            throw new InvalidOperationException($"Failed to compile shader: {SDL3.SDL_GetError()}");
        }

        return new Shader(device, shaderModule,
            new ShaderCreateInfo
            {
                NumSamplers = shaderMetadata.NumSamplers,
                NumStorageTextures = shaderMetadata.NumStorageTextures,
                NumStorageBuffers = shaderMetadata.NumStorageBuffers,
                NumUniformBuffers = shaderMetadata.NumUniformBuffers
            });
    }

    internal static Shader CreateFromHLSL(
        Device device,
        Stream stream,
        string entryPoint,
        string? includeDir,
        ShaderStage shaderStage,
        bool enableDebug,
        string? name,
        params Span<ShaderCross.HLSLDefine> defines)
    {
        byte* hlslBuffer = (byte*)NativeMemory.Alloc((nuint)stream.Length + 1);
        var hlslSpan = new Span<byte>(hlslBuffer, (int)stream.Length);
        stream.ReadExactly(hlslSpan);
        hlslBuffer[(int)stream.Length] = 0; // ensure null-terminated

        byte* entryPointBuffer = MarshalString(entryPoint);
        byte* includeDirBuffer = MarshalString(includeDir);
        byte* nameBuffer = MarshalString(name);

        SDL_ShaderCross.INTERNAL_HLSLDefine* definesBuffer = null;
        if (defines.Length > 0)
        {
            definesBuffer = (SDL_ShaderCross.INTERNAL_HLSLDefine*)NativeMemory.Alloc(
                (nuint)(sizeof(SDL_ShaderCross.INTERNAL_HLSLDefine) * (defines.Length + 1)));
            for (int i = 0; i < defines.Length; i++)
            {
                definesBuffer[i].Name = MarshalString(defines[i].Name);
                definesBuffer[i].Value = MarshalString(defines[i].Value);
            }

            // Null-terminate the array
            definesBuffer[defines.Length].Name = null;
            definesBuffer[defines.Length].Value = null;
        }

        SDL_ShaderCross.INTERNAL_HLSLInfo hlslInfo;
        hlslInfo.Source = hlslBuffer;
        hlslInfo.EntryPoint = entryPointBuffer;
        hlslInfo.IncludeDir = includeDirBuffer;
        hlslInfo.Defines = definesBuffer;
        hlslInfo.ShaderStage = (SDL_ShaderCross.ShaderStage)shaderStage;
        hlslInfo.EnableDebug = enableDebug;
        hlslInfo.Name = nameBuffer;
        hlslInfo.Props = 0;

        SDL_ShaderCross.GraphicsShaderMetadata shaderMetadata;
        var shaderModule = SDL_ShaderCross.SDL_ShaderCross_CompileGraphicsShaderFromHLSL(
            device.Handle, &hlslInfo, &shaderMetadata);

        NativeMemory.Free(hlslBuffer);
        NativeMemory.Free(entryPointBuffer);
        NativeMemory.Free(includeDirBuffer);
        for (int i = 0; i < defines.Length; i++)
        {
            NativeMemory.Free(definesBuffer![i].Name);
            NativeMemory.Free(definesBuffer[i].Value);
        }

        NativeMemory.Free(definesBuffer);
        NativeMemory.Free(nameBuffer);

        if (shaderModule == null)
        {
            throw new InvalidOperationException($"Failed to compile shader: {SDL3.SDL_GetError()}");
        }

        return new Shader(device, shaderModule, new ShaderCreateInfo
        {
            NumSamplers = shaderMetadata.NumSamplers,
            NumStorageTextures = shaderMetadata.NumStorageTextures,
            NumStorageBuffers = shaderMetadata.NumStorageBuffers,
            NumUniformBuffers = shaderMetadata.NumUniformBuffers
        });
    }

    private static byte* MarshalString(string? s)
    {
        if (s == null) return null;

        int length = Encoding.UTF8.GetByteCount(s) + 1;
        byte* buffer = (byte*)NativeMemory.Alloc((nuint)length);
        var span = new Span<byte>(buffer, length);
        int byteCount = Encoding.UTF8.GetBytes(s, span);
        span[byteCount] = 0;

        return buffer;
    }
}
