namespace Beutl.Graphics.Effects;

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
