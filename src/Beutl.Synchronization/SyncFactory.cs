using Beutl.Synchronization.Core;
using Beutl.Synchronization.Managers;
using Beutl.Synchronization.Orchestrators;
using Beutl.Synchronization.Transport;
using Microsoft.Extensions.Logging;

namespace Beutl.Synchronization;

/// <summary>
/// Factory for creating synchronization components
/// </summary>
public static class SyncFactory
{
    /// <summary>
    /// Create a memory-based sync manager for local testing
    /// </summary>
    /// <param name="logger">Optional logger</param>
    /// <returns>Configured sync manager</returns>
    public static ISyncManager CreateMemorySyncManager(ILogger<SyncManager>? logger = null)
    {
        var transport = new MemoryTransport();
        return new SyncManager(transport, logger);
    }

    /// <summary>
    /// Create a project sync orchestrator
    /// </summary>
    /// <param name="syncManager">Sync manager to use</param>
    /// <param name="logger">Optional logger</param>
    /// <returns>Configured orchestrator</returns>
    public static ProjectSyncOrchestrator CreateProjectOrchestrator(
        ISyncManager syncManager, 
        ILogger<ProjectSyncOrchestrator>? logger = null)
    {
        return new ProjectSyncOrchestrator(syncManager, logger);
    }

    /// <summary>
    /// Create a complete synchronization setup for memory-based sync
    /// </summary>
    /// <param name="loggerFactory">Optional logger factory</param>
    /// <returns>Tuple of sync manager and project orchestrator</returns>
    public static (ISyncManager SyncManager, ProjectSyncOrchestrator Orchestrator) CreateMemorySync(
        ILoggerFactory? loggerFactory = null)
    {
        var syncManagerLogger = loggerFactory?.CreateLogger<SyncManager>();
        var orchestratorLogger = loggerFactory?.CreateLogger<ProjectSyncOrchestrator>();

        var syncManager = CreateMemorySyncManager(syncManagerLogger);
        var orchestrator = CreateProjectOrchestrator(syncManager, orchestratorLogger);

        return (syncManager, orchestrator);
    }

    /// <summary>
    /// Create a SignalR-based sync manager
    /// </summary>
    /// <param name="config">Transport configuration</param>
    /// <param name="logger">Optional logger</param>
    /// <returns>Configured sync manager</returns>
    public static ISyncManager CreateSignalRSyncManager(
        SyncTransportConfig config, 
        ILogger<SyncManager>? logger = null)
    {
        var transportLogger = logger?.LoggerFactory?.CreateLogger<SignalRTransport>();
        var transport = new SignalRTransport(config, transportLogger);
        return new SyncManager(transport, logger);
    }
}

/// <summary>
/// Builder pattern for configuring synchronization
/// </summary>
public class SyncConfigurationBuilder
{
    private ISyncTransport? _transport;
    private ILoggerFactory? _loggerFactory;
    private string? _sourceId;

    /// <summary>
    /// Use memory transport (for testing)
    /// </summary>
    /// <returns>Builder for chaining</returns>
    public SyncConfigurationBuilder UseMemoryTransport()
    {
        _transport = new MemoryTransport(_loggerFactory?.CreateLogger<MemoryTransport>());
        return this;
    }

    /// <summary>
    /// Use SignalR transport
    /// </summary>
    /// <param name="config">Transport configuration</param>
    /// <returns>Builder for chaining</returns>
    public SyncConfigurationBuilder UseSignalRTransport(SyncTransportConfig config)
    {
        var transportLogger = _loggerFactory?.CreateLogger<SignalRTransport>();
        _transport = new SignalRTransport(config, transportLogger);
        return this;
    }

    /// <summary>
    /// Configure logging
    /// </summary>
    /// <param name="loggerFactory">Logger factory</param>
    /// <returns>Builder for chaining</returns>
    public SyncConfigurationBuilder WithLogging(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        return this;
    }

    /// <summary>
    /// Set source ID for changes
    /// </summary>
    /// <param name="sourceId">Source identifier</param>
    /// <returns>Builder for chaining</returns>
    public SyncConfigurationBuilder WithSourceId(string sourceId)
    {
        _sourceId = sourceId;
        return this;
    }

    /// <summary>
    /// Build the synchronization setup
    /// </summary>
    /// <returns>Configured sync manager and orchestrator</returns>
    public (ISyncManager SyncManager, ProjectSyncOrchestrator Orchestrator, string SourceId) Build()
    {
        if (_transport == null)
        {
            throw new InvalidOperationException("Transport must be configured. Call UseMemoryTransport() or UseSignalRTransport().");
        }

        var syncManagerLogger = _loggerFactory?.CreateLogger<SyncManager>();
        var orchestratorLogger = _loggerFactory?.CreateLogger<ProjectSyncOrchestrator>();

        var syncManager = new SyncManager(_transport, syncManagerLogger);
        var orchestrator = new ProjectSyncOrchestrator(syncManager, orchestratorLogger);
        var sourceId = _sourceId ?? Environment.MachineName;

        return (syncManager, orchestrator, sourceId);
    }
}

/// <summary>
/// Extension methods for easier configuration
/// </summary>
public static class SyncConfigurationExtensions
{
    /// <summary>
    /// Start building sync configuration
    /// </summary>
    /// <returns>Configuration builder</returns>
    public static SyncConfigurationBuilder ConfigureSync()
    {
        return new SyncConfigurationBuilder();
    }
}