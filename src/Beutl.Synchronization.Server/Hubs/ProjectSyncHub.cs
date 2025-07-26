using System.Collections.Concurrent;
using Beutl.Synchronization.Core;
using Microsoft.AspNetCore.SignalR;

namespace Beutl.Synchronization.Server.Hubs;

/// <summary>
/// SignalR Hub for real-time project synchronization
/// </summary>
public class ProjectSyncHub : Hub
{
    private readonly ILogger<ProjectSyncHub> _logger;
    private static readonly ConcurrentDictionary<string, ProjectSession> _sessions = new();

    public ProjectSyncHub(ILogger<ProjectSyncHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Join a project synchronization session
    /// </summary>
    /// <param name="projectId">Project identifier</param>
    public async Task JoinProject(string projectId)
    {
        try
        {
            _logger.LogInformation("Client {ConnectionId} joining project {ProjectId}", 
                Context.ConnectionId, projectId);

            // Get or create session
            var session = _sessions.GetOrAdd(projectId, id => new ProjectSession(id, _logger));

            // Add client to session
            session.AddClient(Context.ConnectionId);

            // Join SignalR group
            await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(projectId));

            // Notify client of successful join
            await Clients.Caller.SendAsync("SessionJoined", projectId);

            // Notify all clients in session about member count change
            await Clients.Group(GetGroupName(projectId))
                .SendAsync("SessionMemberCountChanged", projectId, session.MemberCount);

            _logger.LogInformation("Client {ConnectionId} successfully joined project {ProjectId}. Total members: {MemberCount}",
                Context.ConnectionId, projectId, session.MemberCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error joining project {ProjectId} for client {ConnectionId}", 
                projectId, Context.ConnectionId);
            
            await Clients.Caller.SendAsync("Error", $"Failed to join project {projectId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Leave the current project synchronization session
    /// </summary>
    /// <param name="projectId">Project identifier</param>
    public async Task LeaveProject(string projectId)
    {
        try
        {
            _logger.LogInformation("Client {ConnectionId} leaving project {ProjectId}", 
                Context.ConnectionId, projectId);

            if (_sessions.TryGetValue(projectId, out var session))
            {
                // Remove client from session
                session.RemoveClient(Context.ConnectionId);

                // Remove from SignalR group
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupName(projectId));

                // Notify client of successful leave
                await Clients.Caller.SendAsync("SessionLeft", projectId);

                // If session is empty, clean it up
                if (session.MemberCount == 0)
                {
                    _sessions.TryRemove(projectId, out _);
                    session.Dispose();
                    _logger.LogInformation("Cleaned up empty session for project {ProjectId}", projectId);
                }
                else
                {
                    // Notify remaining clients about member count change
                    await Clients.Group(GetGroupName(projectId))
                        .SendAsync("SessionMemberCountChanged", projectId, session.MemberCount);
                }

                _logger.LogInformation("Client {ConnectionId} successfully left project {ProjectId}. Remaining members: {MemberCount}",
                    Context.ConnectionId, projectId, session.MemberCount);
            }
            else
            {
                _logger.LogWarning("Client {ConnectionId} tried to leave non-existent project {ProjectId}",
                    Context.ConnectionId, projectId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leaving project {ProjectId} for client {ConnectionId}", 
                projectId, Context.ConnectionId);
        }
    }

    /// <summary>
    /// Send a change notification to other clients in the same project
    /// </summary>
    /// <param name="projectId">Project identifier</param>
    /// <param name="change">Change notification</param>
    public async Task SendChange(string projectId, ChangeNotification change)
    {
        try
        {
            _logger.LogTrace("Relaying change from client {ConnectionId} in project {ProjectId}: {ObjectId}.{PropertyName}",
                Context.ConnectionId, projectId, change.ObjectId, change.PropertyName);

            if (!_sessions.ContainsKey(projectId))
            {
                _logger.LogWarning("Client {ConnectionId} tried to send change to non-existent project {ProjectId}",
                    Context.ConnectionId, projectId);
                return;
            }

            // Ensure the change has the correct session ID
            var changeWithSession = change with { SessionId = Guid.Parse(projectId) };

            // Send to all clients in the project except the sender
            await Clients.GroupExcept(GetGroupName(projectId), Context.ConnectionId)
                .SendAsync("ReceiveChange", changeWithSession);

            _logger.LogTrace("Successfully relayed change to other clients in project {ProjectId}", projectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending change in project {ProjectId} from client {ConnectionId}", 
                projectId, Context.ConnectionId);
        }
    }

    /// <summary>
    /// Get list of active projects
    /// </summary>
    /// <returns>List of project IDs with member counts</returns>
    public async Task<object> GetActiveProjects()
    {
        try
        {
            var activeProjects = _sessions.Select(kvp => new
            {
                ProjectId = kvp.Key,
                MemberCount = kvp.Value.MemberCount,
                CreatedAt = kvp.Value.CreatedAt
            }).ToList();

            _logger.LogDebug("Client {ConnectionId} requested active projects list. Found {ProjectCount} active projects",
                Context.ConnectionId, activeProjects.Count);

            return activeProjects;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active projects for client {ConnectionId}", Context.ConnectionId);
            await Clients.Caller.SendAsync("Error", $"Failed to get active projects: {ex.Message}");
            return Array.Empty<object>();
        }
    }

    /// <summary>
    /// Handle client disconnection
    /// </summary>
    /// <param name="exception">Disconnection exception if any</param>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            _logger.LogInformation("Client {ConnectionId} disconnected. Reason: {Exception}",
                Context.ConnectionId, exception?.Message ?? "Normal disconnection");

            // Remove client from all sessions they were part of
            var sessionsToUpdate = new List<(string ProjectId, ProjectSession Session)>();

            foreach (var kvp in _sessions)
            {
                if (kvp.Value.RemoveClient(Context.ConnectionId))
                {
                    sessionsToUpdate.Add((kvp.Key, kvp.Value));
                }
            }

            // Update member counts and clean up empty sessions
            foreach (var (projectId, session) in sessionsToUpdate)
            {
                if (session.MemberCount == 0)
                {
                    _sessions.TryRemove(projectId, out _);
                    session.Dispose();
                    _logger.LogInformation("Cleaned up empty session for project {ProjectId} after client disconnect", projectId);
                }
                else
                {
                    await Clients.Group(GetGroupName(projectId))
                        .SendAsync("SessionMemberCountChanged", projectId, session.MemberCount);
                }
            }

            await base.OnDisconnectedAsync(exception);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling disconnection for client {ConnectionId}", Context.ConnectionId);
        }
    }

    /// <summary>
    /// Handle client connection
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        try
        {
            _logger.LogInformation("Client {ConnectionId} connected", Context.ConnectionId);
            await base.OnConnectedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling connection for client {ConnectionId}", Context.ConnectionId);
        }
    }

    private static string GetGroupName(string projectId) => $"project_{projectId}";
}

/// <summary>
/// Represents a project synchronization session
/// </summary>
public class ProjectSession : IDisposable
{
    private readonly string _projectId;
    private readonly ILogger _logger;
    private readonly ConcurrentHashSet<string> _clients = new();
    private bool _disposed;

    public ProjectSession(string projectId, ILogger logger)
    {
        _projectId = projectId;
        _logger = logger;
        CreatedAt = DateTime.UtcNow;
    }

    public DateTime CreatedAt { get; }

    public int MemberCount => _clients.Count;

    public void AddClient(string connectionId)
    {
        if (_disposed) return;

        if (_clients.Add(connectionId))
        {
            _logger.LogDebug("Added client {ConnectionId} to project session {ProjectId}. Total members: {MemberCount}",
                connectionId, _projectId, MemberCount);
        }
    }

    public bool RemoveClient(string connectionId)
    {
        if (_disposed) return false;

        if (_clients.Remove(connectionId))
        {
            _logger.LogDebug("Removed client {ConnectionId} from project session {ProjectId}. Remaining members: {MemberCount}",
                connectionId, _projectId, MemberCount);
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _clients.Clear();
        _disposed = true;

        _logger.LogDebug("Project session {ProjectId} disposed", _projectId);
    }
}

/// <summary>
/// Thread-safe HashSet implementation for tracking clients
/// </summary>
public class ConcurrentHashSet<T> where T : notnull
{
    private readonly ConcurrentDictionary<T, byte> _dictionary = new();

    public bool Add(T item) => _dictionary.TryAdd(item, 0);

    public bool Remove(T item) => _dictionary.TryRemove(item, out _);

    public bool Contains(T item) => _dictionary.ContainsKey(item);

    public void Clear() => _dictionary.Clear();

    public int Count => _dictionary.Count;

    public IEnumerable<T> Items => _dictionary.Keys;
}