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
    private readonly Shaderc _shaderc;
    private readonly Compiler* _compiler;
    private readonly CompileOptions* _options;
    private bool _disposed;

    public VulkanShaderCompiler()
    {
        _shaderc = Shaderc.GetApi();
        _compiler = _shaderc.CompilerInitialize();
        _options = _shaderc.CompileOptionsInitialize();

        // Set default options
        _shaderc.CompileOptionsSetOptimizationLevel(_options, OptimizationLevel.Performance);
        _shaderc.CompileOptionsSetTargetEnv(_options, TargetEnv.Vulkan, (uint)EnvVersion.Vulkan12);
        _shaderc.CompileOptionsSetTargetSpirv(_options, SpirvVersion.Shaderc15);
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
            var result = _shaderc.CompileIntoSpv(
                _compiler,
                sourcePtr,
                (nuint)sourceBytes.Length,
                ConvertShaderKind(stage),
                fileNamePtr,
                entryPointPtr,
                _options);

            try
            {
                var status = _shaderc.ResultGetCompilationStatus(result);
                if (status != CompilationStatus.Success)
                {
                    var errorMessagePtr = _shaderc.ResultGetErrorMessage(result);
                    var errorMessage = errorMessagePtr != null
                        ? Marshal.PtrToStringUTF8((IntPtr)errorMessagePtr) ?? "Unknown error"
                        : "Unknown error";
                    throw new InvalidOperationException($"Shader compilation failed: {errorMessage}");
                }

                var length = _shaderc.ResultGetLength(result);
                var bytesPtr = _shaderc.ResultGetBytes(result);

                var spirv = new byte[length];
                Marshal.Copy((IntPtr)bytesPtr, spirv, 0, (int)length);
                return spirv;
            }
            finally
            {
                _shaderc.ResultRelease(result);
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

        if (_options != null)
        {
            _shaderc.CompileOptionsRelease(_options);
        }

        if (_compiler != null)
        {
            _shaderc.CompilerRelease(_compiler);
        }
    }
}
