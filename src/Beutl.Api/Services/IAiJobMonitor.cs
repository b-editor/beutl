using Reactive.Bindings;

namespace Beutl.Api.Services;

/// <summary>
/// Application-layer state and polling around the pure <see cref="IAiJobClient"/>.
/// </summary>
public interface IAiJobMonitor : IBeutlApiResource
{
    IReadOnlyReactiveProperty<AiJobMonitorSnapshot> Snapshot { get; }

    IDisposable AcquirePolling();

    Task RefreshAsync(CancellationToken cancellationToken);

    Task LoadNextPageAsync(CancellationToken cancellationToken);
}
