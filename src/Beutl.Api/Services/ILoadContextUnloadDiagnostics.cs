namespace Beutl.Api.Services;

/// <summary>
/// Captures diagnostics when an extension's collectible <see cref="System.Runtime.Loader.AssemblyLoadContext"/>
/// fails to unload at runtime, so the surviving objects, their GC roots, and the running thread stacks can be logged.
/// </summary>
/// <remarks>
/// Implementations MUST NOT create any managed reference to the extension's assemblies, types, or load context —
/// doing so would itself pin the context and defeat the unload being diagnosed. Only the assembly simple names
/// (plain strings) are passed in; a heap/thread walk must be performed out-of-band (e.g. against a process snapshot).
/// </remarks>
public interface ILoadContextUnloadDiagnostics
{
    /// <summary>
    /// Inspects the current process and records why the extension's load context is still alive.
    /// This is best-effort diagnostics: it must never throw and never alter the uninstall flow.
    /// </summary>
    /// <param name="packageName">The extension package name, used for log context and the dump file name.</param>
    /// <param name="assemblySimpleNames">
    /// The simple names (e.g. <c>MyPlugin</c>) of the assemblies loaded into the leaked context, captured as strings.
    /// </param>
    /// <returns>
    /// The full path of the diagnostics dump that was written, or <see langword="null"/> when none was produced
    /// (e.g. snapshotting is unsupported, no plugin objects survived, or the write failed). Callers may surface this
    /// path so a developer can open the dump; it is a plain string and must never be used to root the leaked context.
    /// </returns>
    string? CaptureUnloadFailure(string packageName, IReadOnlyList<string> assemblySimpleNames);
}
