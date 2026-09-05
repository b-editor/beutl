using System;
using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.Shaderc;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Vulkan implementation of <see cref="IShaderCompiler"/> using shaderc.
/// </summary>
internal sealed unsafe class VulkanShaderCompiler : IShaderCompiler, IDisposable
{
    // libshaderc registers process-wide exit callbacks that outlive dlclose on Linux. Keep one API context
    // loaded for the process; compiler and options handles remain owned by each instance.
    private static readonly Shaderc s_shaderc = Shaderc.GetApi();

    private readonly Compiler* _compiler;
    private readonly CompileOptions* _options;
    private bool _disposed;

    public VulkanShaderCompiler()
    {
        Compiler* compiler = null;
        CompileOptions* options = null;
        try
        {
            compiler = s_shaderc.CompilerInitialize();
            if (compiler == null)
                throw new InvalidOperationException("Failed to initialize the shaderc compiler.");

            options = s_shaderc.CompileOptionsInitialize();
            if (options == null)
                throw new InvalidOperationException("Failed to initialize shaderc compile options.");

            s_shaderc.CompileOptionsSetOptimizationLevel(options, OptimizationLevel.Performance);
            s_shaderc.CompileOptionsSetTargetEnv(options, TargetEnv.Vulkan, (uint)EnvVersion.Vulkan12);
            s_shaderc.CompileOptionsSetTargetSpirv(options, SpirvVersion.Shaderc15);

            _compiler = compiler;
            _options = options;
        }
        catch
        {
            Release(compiler, options);
            throw;
        }
    }

    public byte[] CompileToSpirv(string source, ShaderStage stage, string entryPoint = "main")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var sourceBytes = Encoding.UTF8.GetBytes(source);
        var fileNameBytes = Encoding.UTF8.GetBytes(GetDefaultFileName(stage));
        var entryPointBytes = Encoding.UTF8.GetBytes(entryPoint);

        fixed (byte* sourcePtr = sourceBytes)
        fixed (byte* fileNamePtr = fileNameBytes)
        fixed (byte* entryPointPtr = entryPointBytes)
        {
            var result = s_shaderc.CompileIntoSpv(
                _compiler,
                sourcePtr,
                (nuint)sourceBytes.Length,
                ConvertShaderKind(stage),
                fileNamePtr,
                entryPointPtr,
                _options);

            try
            {
                var status = s_shaderc.ResultGetCompilationStatus(result);
                if (status != CompilationStatus.Success)
                {
                    var errorMessagePtr = s_shaderc.ResultGetErrorMessage(result);
                    var errorMessage = errorMessagePtr != null
                        ? Marshal.PtrToStringUTF8((IntPtr)errorMessagePtr) ?? "Unknown error"
                        : "Unknown error";
                    throw new InvalidOperationException($"Shader compilation failed: {errorMessage}");
                }

                var length = s_shaderc.ResultGetLength(result);
                var bytesPtr = s_shaderc.ResultGetBytes(result);

                var spirv = new byte[length];
                Marshal.Copy((IntPtr)bytesPtr, spirv, 0, (int)length);
                return spirv;
            }
            finally
            {
                s_shaderc.ResultRelease(result);
            }
        }
    }

    private static ShaderKind ConvertShaderKind(ShaderStage stage)
    {
        return stage switch
        {
            ShaderStage.Vertex => ShaderKind.VertexShader,
            ShaderStage.Fragment => ShaderKind.FragmentShader,
            ShaderStage.Compute => ShaderKind.ComputeShader,
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
        };
    }

    private static string GetDefaultFileName(ShaderStage stage)
    {
        return stage switch
        {
            ShaderStage.Vertex => "shader.vert",
            ShaderStage.Fragment => "shader.frag",
            ShaderStage.Compute => "shader.comp",
            _ => "shader.glsl"
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Release(_compiler, _options);
    }

    private static void Release(Compiler* compiler, CompileOptions* options)
    {
        try
        {
            if (options != null)
            {
                s_shaderc.CompileOptionsRelease(options);
            }
        }
        finally
        {
            if (compiler != null)
            {
                s_shaderc.CompilerRelease(compiler);
            }
        }
    }
}
