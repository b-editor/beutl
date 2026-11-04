namespace Beutl.NodeGraph.Generative;

/// <summary>
/// Runs generation requests for the node graph. Implemented by the application, which
/// owns the API client, the billing safeguards and where results are stored; the graph
/// only describes what to ask for.
/// </summary>
public interface IGenerativeNodeExecutor
{
    /// <summary>
    /// Runs one request. Throws <see cref="GenerativeExecutionException"/> with a message
    /// for the user when it fails for a known reason, and
    /// <see cref="OperationCanceledException"/> when cancelled.
    /// </summary>
    Task<GenerativeExecutionResult> ExecuteAsync(
        GenerativeRequest request,
        IProgress<GenerativeProgress> progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// Checks a request as <see cref="ExecuteAsync"/> checks it before anything is reserved, and
    /// returns why it would be refused, or null when it would not. Nothing is sent or charged.
    /// </summary>
    Task<GenerativeRefusal?> ValidateAsync(GenerativeRequest request, CancellationToken cancellationToken)
        => Task.FromResult<GenerativeRefusal?>(null);
}
