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

/// <summary>
/// Result of validating a candidate script. <see cref="Unavailable"/> is reported separately from success so callers do
/// not mistake "could not check" for "valid".
/// </summary>
public readonly record struct ScriptCompilationResult(ScriptCompilationStatus Status, string? Error)
{
    public static ScriptCompilationResult Compiled { get; } = new(ScriptCompilationStatus.Compiled, null);

    public static ScriptCompilationResult Unavailable { get; } = new(ScriptCompilationStatus.Unavailable, null);

    public static ScriptCompilationResult Fail(string error) => new(ScriptCompilationStatus.Failed, error);
}

/// <summary>
/// Implemented by filter effects whose primary parameter is a compilable script (a shader or code), letting tooling
/// validate a candidate script without rendering. The method is intentionally an instance member so callers can dispatch
/// over a runtime <see cref="System.Type"/> resolved from the effect registry; it does not depend on instance state.
/// </summary>
public interface IScriptCompilableEffect
{
    /// <summary>
    /// Validates <paramref name="script"/> against this effect's compiler. An empty or whitespace script is treated as
    /// <see cref="ScriptCompilationStatus.Compiled"/>.
    /// </summary>
    ScriptCompilationResult ValidateScript(string script);
}
