using System;

namespace Beutl.Graphics.Backend;

/// <summary>
/// Interface for shader compilation.
/// </summary>
/// <remarks>
/// An instance returned by <see cref="IGraphicsContext.CreateShaderCompiler"/> is caller-owned. Implementations
/// that own native state may additionally implement <see cref="IDisposable"/> and must then be disposed by the
/// caller.
/// </remarks>
public interface IShaderCompiler
{
    /// <summary>
    /// Compiles shader source code to SPIR-V bytecode.
    /// </summary>
    /// <param name="source">The shader source code.</param>
    /// <param name="stage">The shader stage.</param>
    /// <param name="entryPoint">The entry point function name.</param>
    /// <returns>The compiled SPIR-V bytecode.</returns>
    byte[] CompileToSpirv(string source, ShaderStage stage, string entryPoint = "main");
}
