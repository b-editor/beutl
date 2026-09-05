namespace Beutl.Graphics.Effects;

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
