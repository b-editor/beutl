namespace Beutl.Graphics.Effects;

/// <summary>
/// The outcome of compiling a candidate script for an <see cref="IScriptCompilableEffect"/>.
/// </summary>
public enum ScriptCompilationStatus
{
    /// <summary>The script compiled without errors.</summary>
    Compiled,

    /// <summary>The script failed to compile; <see cref="ScriptCompilationResult.Error"/> carries the compiler message.</summary>
    Failed,

    /// <summary>
    /// Compilation could not be attempted in the current environment (for example, a GPU shader compiler that needs a
    /// graphics context that is not available headlessly). This is distinct from <see cref="Compiled"/>: the script was
    /// neither accepted nor rejected.
    /// </summary>
    Unavailable,
}
