using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

using Reactive.Bindings;

namespace Beutl.Extensibility;

/// <summary>Provides the settings, progress, and complete execution body for one output profile.</summary>
public interface IOutputContext : IDisposable, IJsonSerializable
{
    OutputExtension Extension { get; }

    CoreObject Object { get; }

    IReactiveProperty<string> Name { get; }

    IReadOnlyReactiveProperty<bool> IsIndeterminate { get; }

    IReadOnlyReactiveProperty<double> Progress { get; }

    /// <summary>Runs the complete output operation.</summary>
    /// <remarks>
    /// The returned task must not complete until all output I/O and cleanup have finished. Controls
    /// must request execution through the host-provided <see cref="IOutputExecutionController"/>
    /// instead of calling this method directly.
    /// </remarks>
    Task RunAsync(CancellationToken cancellationToken);
}

/// <summary>Controls host-admitted execution of an output profile.</summary>
public interface IOutputExecutionController
{
    /// <summary>Gets whether the host is currently running the output context.</summary>
    IReadOnlyReactiveProperty<bool> IsRunning { get; }

    /// <summary>
    /// Tries to admit output execution and returns its complete lifetime task when accepted.
    /// </summary>
    /// <remarks>
    /// A <see langword="false"/> result means the workspace is busy; <paramref name="execution"/>
    /// is <see langword="null"/> and the output context is not entered. An already-running profile
    /// returns <see langword="true"/> and the same single-flight task. Await that task through all
    /// output I/O and cleanup. Use <see cref="Cancel"/> to request cancellation; callers that only
    /// want to stop waiting can await <c>execution.WaitAsync(cancellationToken)</c>.
    /// </remarks>
    bool TryStart([NotNullWhen(true)] out Task? execution);

    /// <summary>Requests cancellation of the active execution.</summary>
    /// <remarks>
    /// The request is idempotent and has no effect while idle. If the context cooperatively
    /// propagates cancellation, the execution task is canceled. A context that ignores the request
    /// keeps its task and workspace lease active until <see cref="IOutputContext.RunAsync"/> returns.
    /// </remarks>
    void Cancel();
}

public interface ISupportOutputPreset
{
    void Apply(JsonObject preset);

    JsonObject ToPreset();
}

public abstract class OutputExtension : Extension
{
    public abstract FilePickerFileType GetFilePickerFileType();

    /// <summary>Creates the control for one output profile.</summary>
    /// <remarks>
    /// The control must use <paramref name="execution"/> for start and cancellation requests. A new
    /// control is requested for each profile and must not retain another profile's context or
    /// controller.
    /// </remarks>
    public abstract bool TryCreateControl(
        IEditorContext editorContext,
        IOutputContext context,
        IOutputExecutionController execution,
        [NotNullWhen(true)] out Control? control);

    /// <summary>Creates the serializable context for one output profile.</summary>
    public abstract bool TryCreateContext(
        IEditorContext editorContext,
        [NotNullWhen(true)] out IOutputContext? context);

    public abstract bool IsSupported(Type type);
}
