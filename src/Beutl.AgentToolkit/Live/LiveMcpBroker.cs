using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit.Live;

// Live routing for the stdio MCP server. A call that names an instanceId is forwarded to that
// running Beutl editor after proving its identity with the profile's live MCP token; a call without
// one runs the server's own headless tools on project files. The agent chooses per call, and a
// call to an exited process fails instead of reaching a replacement or the headless editor.
public sealed class LiveMcpBroker
{
    public const string ListInstancesToolName = "list_instances";
    private const string InstanceIdArgument = "instanceId";
    private const string SceneIdArgument = "sceneId";
    private const string SessionArgument = "session";

    private static readonly TimeSpan s_defaultWatchInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan s_maxRetryDelay = TimeSpan.FromMinutes(1);

    private readonly AgentHostInstanceRegistry _registry;
    private readonly Func<string> _tokenProvider;
    private readonly ILogger _logger;
    private readonly string _brokerId = Guid.NewGuid().ToString("N");
    private readonly object _lock = new();
    private AgentHostPeerClient? _peers;
    private McpServer? _server;
    private string[]? _advertisedKeys;
    // Editors that did not answer tools/list, with the next time the watcher asks the client to
    // list again. The delay doubles from one interval up to a minute until the editor answers.
    private readonly Dictionary<string, (int Attempts, DateTime NextAt)> _unreachable = new(StringComparer.Ordinal);

    public LiveMcpBroker(AgentHostInstanceRegistry registry, Func<string> tokenProvider, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(tokenProvider);
        _registry = registry;
        _tokenProvider = tokenProvider;
        _logger = logger ?? NullLogger.Instance;
    }

    // Reads the same discovery directory and token store the editors of that profile use. The
    // token is resolved lazily so a missing or invalid store surfaces as a tool error, not a crash.
    public static LiveMcpBroker CreateForProfile(string profileDirectory, ILogger? logger = null)
        => new(
            new AgentHostInstanceRegistry(AgentHostInstanceRegistry.GetDefaultDirectory(profileDirectory)),
            () => LiveMcpTokenStore.GetOrCreate(profileDirectory),
            logger);

    public void ConfigureServer(McpServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ServerInstructions =
            "Beutl video editor. Call list_instances first. If the project the user is working on is open in a "
            + "running Beutl, pass that instance's instanceId on every call so edits happen live in its editor; "
            + "scene operations there also take sceneId from list_scenes. Without instanceId, calls work headlessly "
            + "on project files through file sessions returned by open_project/create_project. File paths passed to "
            + "tools must be absolute. "
            + "A call naming an exited instanceId returns instance_unavailable rather than editing another project.";
        options.Capabilities ??= new ServerCapabilities();
        options.Capabilities.Tools ??= new ToolsCapability();
        options.Capabilities.Tools.ListChanged = true;
    }

    // Wraps the server's own tool pipeline: tools/list gains live targeting, tools/call is routed.
    public static void AddFilters(IMcpRequestFilterBuilder filters)
    {
        filters.AddListToolsFilter(next => async (context, cancellationToken) =>
        {
            ListToolsResult result = await next(context, cancellationToken).ConfigureAwait(false);
            var broker = context.Services!.GetRequiredService<LiveMcpBroker>();
            return await broker.DescribeToolsAsync(context.Server, result, cancellationToken).ConfigureAwait(false);
        });

        filters.AddCallToolFilter(next => (context, cancellationToken) =>
        {
            var broker = context.Services!.GetRequiredService<LiveMcpBroker>();
            return broker.RouteAsync(context, next, cancellationToken);
        });
    }

    // Sends tools/list_changed while editors start and exit, so an agent that connected before
    // Beutl was open (or whose editor was replaced) refreshes the live-only tools.
    public Task WatchAsync(CancellationToken cancellationToken)
        => WatchAsync(s_defaultWatchInterval, cancellationToken);

    public async Task WatchAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                string[] current = ReadAdvertisedKeys();
                McpServer? server;
                bool changed;
                lock (_lock)
                {
                    // Compare against the set the last tools/list answer was based on; before any
                    // tools/list there is nothing a client could have cached.
                    changed = _advertisedKeys is not null && !current.SequenceEqual(_advertisedKeys);
                    if (changed)
                        _advertisedKeys = current;
                    DateTime now = DateTime.UtcNow;
                    foreach ((string id, (int attempts, DateTime nextAt)) in _unreachable.ToArray())
                    {
                        if (!current.Any(key => key.StartsWith(id + ":", StringComparison.Ordinal)))
                            _unreachable.Remove(id);
                        else if (nextAt <= now)
                        {
                            changed = true;
                            long delay = Math.Min(interval.Ticks * (1L << Math.Min(attempts, 16)), s_maxRetryDelay.Ticks);
                            _unreachable[id] = (attempts + 1, now + TimeSpan.FromTicks(delay));
                        }
                    }

                    server = _server;
                }

                if (changed && server is not null)
                {
                    try
                    {
                        await server.SendMessageAsync(
                            new JsonRpcNotification { Method = NotificationMethods.ToolListChangedNotification },
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogDebug(ex, "Could not notify the client that the MCP tools changed.");
                    }
                }

                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public async Task<ToolResult<ListAgentHostInstancesResponse>> ListInstancesAsync(CancellationToken cancellationToken)
    {
        if (!TryGetPeers(out AgentHostPeerClient? peers, out ToolError? unavailable))
            return new ToolResult<ListAgentHostInstancesResponse>(null, unavailable);

        AgentHostInstanceRegistration[] registrations = _registry.Read().ToArray();
        AgentHostInstanceInfo?[] infos = await Task.WhenAll(
            registrations.Select(entry => peers.ReadInfoAsync(entry, cancellationToken))).ConfigureAwait(false);
        // No connected instance: this server never picks a process on the agent's behalf.
        return ToolResult<ListAgentHostInstancesResponse>.Success(new ListAgentHostInstancesResponse(null,
            infos.OfType<AgentHostInstanceInfo>()
                .OrderBy(info => info.ProcessId).ThenBy(info => info.InstanceId, StringComparer.Ordinal).ToArray()));
    }

    internal async ValueTask<ListToolsResult> DescribeToolsAsync(
        McpServer server, ListToolsResult local, CancellationToken cancellationToken)
    {
        Attach(server);
        var tools = new List<Tool>();
        var localNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (Tool tool in local.Tools)
        {
            localNames.Add(tool.Name);
            tools.Add(tool.Name == ListInstancesToolName ? tool : WithLiveTarget(tool));
        }

        IReadOnlyList<AgentHostInstanceRegistration> registrations = _registry.Read();
        var unreachable = new List<string>();

        // Tools that exist only inside an editor (scene discovery, AI generation, extension tools)
        // always need an instanceId. Every running instance is asked, oldest first, so a package
        // loaded in only one editor is still listed and one unresponsive editor hides nothing.
        if (registrations.Count > 0 && TryGetPeers(out AgentHostPeerClient? peers, out _))
        {
            AgentHostInstanceRegistration[] ordered = OldestFirst(registrations).ToArray();
            IList<Tool>?[] remote = await Task.WhenAll(ordered
                .Select(registration => peers.ListToolsAsync(registration, cancellationToken))).ConfigureAwait(false);
            for (int i = 0; i < ordered.Length; i++)
            {
                if (remote[i] is not { } listed)
                {
                    unreachable.Add(ordered[i].InstanceId);
                    continue;
                }

                foreach (Tool tool in listed)
                {
                    if (localNames.Add(tool.Name))
                        tools.Add(RequireInstance(tool));
                }
            }
        }

        lock (_lock)
        {
            _advertisedKeys = registrations.Select(AdvertisedKey).Order(StringComparer.Ordinal).ToArray();
            // An editor that did not answer is asked for again: the watcher announces a tools change
            // with growing delays until the editor answers or exits, so a transient failure never
            // leaves its tools out of the client's list for good.
            foreach (string id in _unreachable.Keys.Where(id => !unreachable.Contains(id)).ToArray())
                _unreachable.Remove(id);
            foreach (string id in unreachable)
                _unreachable.TryAdd(id, (0, DateTime.UtcNow));
        }

        local.Tools = tools;
        return local;
    }

    internal async ValueTask<CallToolResult> RouteAsync(
        RequestContext<CallToolRequestParams> context,
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        CancellationToken cancellationToken)
    {
        Attach(context.Server);
        CallToolRequestParams? request = context.Params;
        if (request is null)
            return await next(context, cancellationToken).ConfigureAwait(false);

        IDictionary<string, JsonElement>? arguments = request.Arguments;
        JsonElement id = default;
        bool hasInstanceId = arguments is not null && arguments.TryGetValue(InstanceIdArgument, out id);
        if (!hasInstanceId)
        {
            if (arguments is not null && arguments.ContainsKey(SceneIdArgument))
                return SceneWithoutInstance();
            if (!IsLocalTool(context.Server, request.Name))
                return LiveOnly(request.Name);
            // Headless: the server's own file-backed tools, exactly like the former stdio server.
            return await next(context, cancellationToken).ConfigureAwait(false);
        }

        if (request.Name == ListInstancesToolName || id.ValueKind != JsonValueKind.String
            || !Guid.TryParseExact(id.GetString(), "N", out _))
            return LiveToolResults.InvalidInstanceId();

        if (!TryGetPeers(out AgentHostPeerClient? peers, out ToolError? unavailable))
            return LiveToolResults.Error(unavailable);

        // An explicit target never moves: an exited process fails here even if a replacement is
        // registered, so an edit cannot land in another project or fall back to the headless editor.
        string instanceId = id.GetString()!;
        return await peers.ForwardAsync(instanceId, WithoutInstanceId(request, arguments!), context.Server, cancellationToken)
            .ConfigureAwait(false);
    }

    private bool TryGetPeers([NotNullWhen(true)] out AgentHostPeerClient? peers, [NotNullWhen(false)] out ToolError? unavailable)
    {
        lock (_lock)
        {
            if (_peers is null)
            {
                try
                {
                    _peers = new AgentHostPeerClient(_registry, new AgentHostInstanceAuthentication(_tokenProvider(), _brokerId));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    _logger.LogWarning(ex, "The live MCP token of the Beutl profile could not be read.");
                    peers = null;
                    unavailable = new ToolError(ErrorCode.LiveUnavailable,
                        "The live MCP credentials of this Beutl profile could not be read: " + ex.Message,
                        "token",
                        "Start Beutl once so it creates the profile's live MCP token, or point BEUTL_HOME of this server entry at the profile Beutl uses. Headless calls without instanceId still work.");
                    return false;
                }
            }

            peers = _peers;
            unavailable = null;
            return true;
        }
    }

    private void Attach(McpServer server)
    {
        lock (_lock)
            _server ??= server;
    }

    // Instance ID plus tools version: a package that added or removed tools in a running editor is
    // announced the same way a started or exited editor is.
    private string[] ReadAdvertisedKeys()
        => _registry.Read().Select(AdvertisedKey).Order(StringComparer.Ordinal).ToArray();

    private static string AdvertisedKey(AgentHostInstanceRegistration registration)
        => registration.InstanceId + ":" + registration.ToolsVersion.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static IEnumerable<AgentHostInstanceRegistration> OldestFirst(IReadOnlyList<AgentHostInstanceRegistration> registrations)
        => registrations
            .OrderBy(entry => entry.ProcessStartTime)
            .ThenBy(entry => entry.InstanceId, StringComparer.Ordinal);

    private static bool IsLocalTool(McpServer server, string name)
        => server.ServerOptions.ToolCollection is { } collection && collection.TryGetPrimitive(name, out _);

    // The headless schema plus the live target: instanceId selects a running editor, sceneId its
    // scene, and the file session stops being required because live calls never carry one.
    private static Tool WithLiveTarget(Tool tool)
    {
        JsonObject node = JsonSerializer.SerializeToNode(tool)!.AsObject();
        JsonObject schema = node["inputSchema"]!.AsObject();
        var properties = schema["properties"] as JsonObject;
        if (properties is null)
            schema["properties"] = properties = new JsonObject();
        properties[InstanceIdArgument] = new JsonObject
        {
            ["type"] = "string",
            ["description"] = "ID of the running Beutl instance (from list_instances) to run this call in; the edit then appears live in its editor. Omit to work headlessly on project files."
        };
        // Only the tools the editor scene-routes take a scene; on any other tool the editor would
        // reject sceneId as an unknown argument.
        if (AgentHostSceneRouting.AcceptsScene(tool.Name))
        {
            properties[SceneIdArgument] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Live only: ID of the target scene in that instance (from list_scenes there). Pass it together with instanceId; omit it for headless file sessions."
            };
        }

        if (properties[SessionArgument] is JsonObject session)
        {
            string description = session["description"]?.GetValue<string>() ?? "";
            session["description"] = (description.Length == 0 ? "" : description.TrimEnd() + " ")
                                     + "Headless only; omit when instanceId is given.";
        }

        if (schema["required"] is JsonArray required)
        {
            JsonNode? sessionRequirement = required.FirstOrDefault(item => item?.GetValue<string>() == SessionArgument);
            if (sessionRequirement is not null)
                required.Remove(sessionRequirement);
        }

        return node.Deserialize<Tool>()!;
    }

    private static Tool RequireInstance(Tool tool)
    {
        JsonObject node = JsonSerializer.SerializeToNode(tool)!.AsObject();
        JsonObject schema = node["inputSchema"]!.AsObject();
        var properties = schema["properties"] as JsonObject;
        if (properties is null)
            schema["properties"] = properties = new JsonObject();
        properties[InstanceIdArgument] ??= new JsonObject
        {
            ["type"] = "string",
            ["description"] = "ID of the running Beutl instance (from list_instances) that runs this tool."
        };
        var required = schema["required"] as JsonArray;
        if (required is null)
            schema["required"] = required = new JsonArray();
        if (!required.Any(item => item?.GetValue<string>() == InstanceIdArgument))
            required.Add(InstanceIdArgument);
        node["description"] = "Live only (requires instanceId). " + (tool.Description ?? "");
        return node.Deserialize<Tool>()!;
    }

    private static CallToolRequestParams WithoutInstanceId(
        CallToolRequestParams request, IDictionary<string, JsonElement> arguments)
    {
        var forwarded = new Dictionary<string, JsonElement>(arguments, StringComparer.Ordinal);
        forwarded.Remove(InstanceIdArgument);
        return new CallToolRequestParams
        {
            Name = request.Name,
            Arguments = forwarded,
            Meta = request.Meta,
            InputResponses = request.InputResponses,
            RequestState = request.RequestState
        };
    }

    private static CallToolResult SceneWithoutInstance()
        => LiveToolResults.Error(ErrorCode.ValidationRejected,
            "sceneId targets a scene in a running Beutl instance; pass instanceId as well.", InstanceIdArgument,
            "Call list_instances for the instanceId, or omit sceneId and use a headless session from open_project/create_project.");

    private static CallToolResult LiveOnly(string toolName)
        => LiveToolResults.Error(ErrorCode.ValidationRejected,
            $"'{toolName}' runs only inside a running Beutl instance.", InstanceIdArgument,
            "Call list_instances and pass the instanceId of the instance to use.");
}
