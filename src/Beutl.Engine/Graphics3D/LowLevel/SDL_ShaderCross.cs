using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SDL;

namespace Beutl.Graphics3D;

internal static unsafe partial class SDL_ShaderCross
{
    private const string NativeLibName = "SDL3_shadercross";


    public enum ShaderStage
    {
        Vertex,
        Fragment,
        Compute
    }

    public struct GraphicsShaderMetadata
    {
        public uint NumSamplers;
        public uint NumStorageTextures;
        public uint NumStorageBuffers;
        public uint NumUniformBuffers;
    }

    public struct ComputePipelineMetadata
    {
        public uint NumSamplers;
        public uint NumReadOnlyStorageTextures;
        public uint NumReadOnlyStorageBuffers;
        public uint NumReadWriteStorageTextures;
        public uint NumReadWriteStorageBuffers;
        public uint NumUniformBuffers;
        public uint ThreadCountX;
        public uint ThreadCountY;
        public uint ThreadCountZ;
    }

    public struct INTERNAL_SPIRVInfo
    {
        public byte* Bytecode;
        public UIntPtr BytecodeSize;
        public byte* EntryPoint;
        public ShaderStage ShaderStage;
        public SDLBool EnableDebug;
        public byte* Name;
        public uint Props;
    }

    public struct INTERNAL_HLSLDefine
    {
        public byte* Name;
        public byte* Value;
    }

    public struct INTERNAL_HLSLInfo
    {
        public byte* Source;
        public byte* EntryPoint;
        public byte* IncludeDir;
        public INTERNAL_HLSLDefine* Defines;
        public ShaderStage ShaderStage;
        public SDLBool EnableDebug;
        public byte* Name;
        public uint Props;
    }

    [LibraryImport(NativeLibName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial byte SDL_ShaderCross_Init();
    // public static partial SDLBool SDL_ShaderCross_Init();

    [LibraryImport(NativeLibName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void SDL_ShaderCross_Quit();

    [LibraryImport(NativeLibName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial ShaderFormat SDL_ShaderCross_GetSPIRVShaderFormats();

    [LibraryImport(NativeLibName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr SDL_ShaderCross_TranspileMSLFromSPIRV(INTERNAL_SPIRVInfo* info);

    [LibraryImport(NativeLibName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr SDL_ShaderCross_TranspileHLSLFromSPIRV(INTERNAL_SPIRVInfo* info);

    [LibraryImport(NativeLibName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr SDL_ShaderCross_CompileDXBCFromSPIRV(INTERNAL_SPIRVInfo* info, UIntPtr* size);

    [LibraryImport(NativeLibName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr SDL_ShaderCross_CompileDXILFromSPIRV(INTERNAL_SPIRVInfo* info, UIntPtr* size);

    [LibraryImport(NativeLibName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial SDL_GPUShader* SDL_ShaderCross_CompileGraphicsShaderFromSPIRV(
        SDL_GPUDevice* device, INTERNAL_SPIRVInfo* info, GraphicsShaderMetadata* metadata);

    [LibraryImport(NativeLibName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr SDL_ShaderCross_CompileComputePipelineFromSPIRV(
        SDL_GPUDevice* device, INTERNAL_SPIRVInfo* info, ComputePipelineMetadata* metadata);

    [LibraryImport(NativeLibName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial ShaderFormat SDL_ShaderCross_GetHLSLShaderFormats();

    [LibraryImport(NativeLibName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr SDL_ShaderCross_CompileDXBCFromHLSL(INTERNAL_HLSLInfo* info, UIntPtr* size);

    [LibraryImport(NativeLibName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr SDL_ShaderCross_CompileDXILFromHLSL(INTERNAL_HLSLInfo* info, UIntPtr* size);

    [LibraryImport(NativeLibName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr SDL_ShaderCross_CompileSPIRVFromHLSL(INTERNAL_HLSLInfo* info, UIntPtr* size);

    [LibraryImport(NativeLibName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial SDL_GPUShader* SDL_ShaderCross_CompileGraphicsShaderFromHLSL(
        SDL_GPUDevice* device, INTERNAL_HLSLInfo* info, GraphicsShaderMetadata* metadata);

    [LibraryImport(NativeLibName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial IntPtr SDL_ShaderCross_CompileComputePipelineFromHLSL(
        SDL_GPUDevice* device, INTERNAL_HLSLInfo* info, ComputePipelineMetadata* metadata);

    [LibraryImport(NativeLibName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial byte SDL_ShaderCross_ReflectGraphicsSPIRV(
        Span<byte> bytecode, UIntPtr bytecodeSize, GraphicsShaderMetadata* info);

    [LibraryImport(NativeLibName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial byte SDL_ShaderCross_ReflectComputeSPIRV(
        Span<byte> bytecode, UIntPtr bytecodeSize, ComputePipelineMetadata* info);
}
