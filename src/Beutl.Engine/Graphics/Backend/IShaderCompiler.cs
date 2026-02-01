using System;

namespace Beutl.Graphics.Backend;

/// <summary>
/// Interface for shader compilation.
/// </summary>
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
