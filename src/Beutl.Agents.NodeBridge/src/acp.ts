import {
    ClientSideConnection, ndJsonStream, PROTOCOL_VERSION,
    type McpServer, type PermissionOptionKind, type RequestPermissionRequest, type RequestPermissionResponse,
    type SessionConfigOption, type SessionConfigSelectOption, type SessionNotification, type SessionModeState, type ToolCallStatus, type ToolCallUpdate,
} from "@agentclientprotocol/sdk";
import { Readable, Writable } from "node:stream";
import { ProviderProcess } from "./process.js";
import { type Adapter, type AdapterHost, type ComposerSuggestion, type Decision, type ModelDescriptor, type PermissionMode, type Turn, type TurnResult, contentOutputs, planStatus, record, withTimeout } from "./protocol.js";

const decisions: Record<PermissionOptionKind, Decision> = {
    allow_once: "allowOnce", allow_always: "allowAlways", reject_once: "rejectOnce", reject_always: "rejectAlways",
};

interface LegacyModels {
    currentModelId: string;
    availableModels: { modelId: string; name: string }[];
}

type ConfigSelect = Extract<SessionConfigOption, { type: "select" }>;

function values(option?: ConfigSelect): SessionConfigSelectOption[] {
    return option?.options.flatMap(item => "options" in item ? item.options : [item]) ?? [];
}

export class AcpAdapter implements Adapter {
    private process?: ProviderProcess;
    private connection?: ClientSideConnection;
    private sessionId?: string;
    private modes?: SessionModeState | null;
    private configOptions: SessionConfigOption[] = [];
    private legacyModels?: LegacyModels | null;
    private initialModel?: string;
    private configuredModel?: string;
    private configuredEffort?: string;
    private readonly effortDefaults = new Map<string | undefined, string>();
    private canCloseSession = false;
    private active?: Turn;
    private message = "";
    private messageId?: string;
    private readonly tools = new Map<string, ToolCallUpdate>();
    private suggestions?: ComposerSuggestion[];
    private earlySuggestions?: { sessionId: string; suggestions: ComposerSuggestion[] };
    private readonly commandsReceived = Promise.withResolvers<void>();

    constructor(private readonly host: AdapterHost) {}

    async open(): Promise<void> {
        const options = this.host.options;
        const child = this.process = new ProviderProcess(this.host, options.executablePath, options.arguments ?? []);
        void child.closed.then(message => this.host.disconnected(message));
        const stream = ndJsonStream(Writable.toWeb(child.child.stdin), Readable.toWeb(child.child.stdout) as ReadableStream<Uint8Array>);
        const client = this.connection = new ClientSideConnection(() => ({
            requestPermission: request => this.permission(request),
            sessionUpdate: notification => this.update(notification),
        }), stream);
        const initialized = await Promise.race([
            client.initialize({
                protocolVersion: PROTOCOL_VERSION,
                clientInfo: { name: "beutl", title: "Beutl", version: "1" },
                clientCapabilities: {},
            }),
            child.closed.then(message => { throw new Error(message); }),
        ]);
        if (initialized.protocolVersion !== PROTOCOL_VERSION)
            throw new Error("Unsupported ACP protocol version " + initialized.protocolVersion + ".");
        this.canCloseSession = !!initialized.agentCapabilities?.sessionCapabilities?.close;

        const mcpServers: McpServer[] = options.mcpServers.map(server => {
            const { name } = server;
            if (server.type === "http") {
                if (!initialized.agentCapabilities?.mcpCapabilities?.http)
                    throw new Error("This ACP agent does not support HTTP MCP servers.");
                const headers = Object.entries(server.headers ?? {}).map(([name, value]) => ({ name, value }));
                return { type: "http", name, url: server.url, headers };
            }
            const env = Object.entries(server.env ?? {}).map(([name, value]) => ({ name, value }));
            return { name, command: server.command, args: server.args ?? [], env };
        });
        const request = { cwd: options.workingDirectory, mcpServers };
        let session;
        if (options.externalSessionId) {
            if (!initialized.agentCapabilities?.loadSession)
                throw new Error("This ACP agent does not support session/load.");
            this.sessionId = options.externalSessionId;
            session = await client.loadSession({ ...request, sessionId: this.sessionId });
        } else {
            session = await client.newSession(request);
            this.sessionId = session.sessionId;
        }
        if (this.earlySuggestions?.sessionId === this.sessionId)
            this.setSuggestions(this.earlySuggestions.suggestions);
        this.earlySuggestions = undefined;
        this.modes = session.modes;
        this.configOptions = session.configOptions ?? [];
        this.legacyModels = (session as { models?: LegacyModels }).models;
        this.initialModel = this.config("model")?.currentValue ?? this.legacyModels?.currentModelId;
        this.rememberEffortDefault(this.initialModel);
        this.host.saveSessionId(this.sessionId);
        await this.setMode(options.permissionMode);
    }

    listModels(providerId: string): ModelDescriptor[] {
        const effort = this.config("thought_level");
        const model = this.config("model");
        const models: { value: string; name: string }[] = model ? values(model)
            : (this.legacyModels?.availableModels ?? []).map(item => ({ value: item.modelId, name: item.name }));
        return models.map((item, index) => ({
            id: item.value,
            displayName: item.name,
            shortName: item.name,
            providerId,
            reasoningEfforts: values(effort).map(level => level.value),
            defaultReasoningEffort: this.effortDefaults.get(item.value),
            isProviderDefault: item.value === this.initialModel,
            isSecondary: index >= 4,
        }));
    }

    async listSuggestions(): Promise<ComposerSuggestion[]> {
        if (!this.suggestions)
            await withTimeout(this.commandsReceived.promise, 1250, "ACP did not report any commands.").catch(() => {});
        return this.suggestions ?? [];
    }

    private setSuggestions(suggestions: ComposerSuggestion[]): void {
        this.suggestions = suggestions;
        this.commandsReceived.resolve();
        this.host.publishSuggestions(suggestions);
    }

    async setModel(model?: string, effort?: string): Promise<void> {
        const { connection, sessionId } = this;
        if (!connection || !sessionId || (model === this.configuredModel && effort === this.configuredEffort)) return;
        const selector = this.config("model");
        const id = model ?? this.initialModel;
        if (id && selector) {
            await this.setConfig(selector, id);
        } else if (id && id !== this.legacyModels?.currentModelId) {
            // Older ACP agents exposed this experimental request before configOptions.
            await connection.request("session/set_model", { sessionId, modelId: id });
            if (this.legacyModels) this.legacyModels.currentModelId = id;
        }

        const effortSelector = this.config("thought_level");
        if (effort && !effortSelector) throw new Error("This ACP session does not expose a reasoning effort setting.");
        this.rememberEffortDefault(id);
        const level = effort ?? this.effortDefaults.get(id);
        if (effortSelector && level !== undefined)
            await this.setConfig(effortSelector, level);
        this.configuredModel = model;
        this.configuredEffort = effort;
    }

    private rememberEffortDefault(model?: string): void {
        const effort = this.config("thought_level")?.currentValue;
        if (effort !== undefined && !this.effortDefaults.has(model))
            this.effortDefaults.set(model, effort);
    }

    private config(category: string): ConfigSelect | undefined {
        return this.configOptions.find(option => option.type === "select" && option.category === category) as ConfigSelect | undefined;
    }

    private async setConfig(option: ConfigSelect, value: string): Promise<void> {
        if (option.currentValue === value || !this.connection || !this.sessionId) return;
        const response = await this.connection.setSessionConfigOption({ sessionId: this.sessionId, configId: option.id, value });
        this.configOptions = response.configOptions;
    }

    async run(turn: Turn): Promise<TurnResult> {
        if (!this.connection || !this.sessionId) throw new Error("ACP session is closed.");
        this.active = turn;
        this.tools.clear();
        try {
            const result = await this.connection.prompt({
                sessionId: this.sessionId,
                prompt: [{ type: "text", text: turn.prompt }],
            });
            return { status: result.stopReason === "cancelled" ? "cancelled" : "completed" };
        } finally {
            this.completeMessage();
            this.active = undefined;
        }
    }

    async steer(): Promise<boolean> {
        return false;
    }

    async cancel(): Promise<void> {
        if (this.sessionId)
            await this.connection?.cancel({ sessionId: this.sessionId });
    }

    async setMode(mode: PermissionMode): Promise<void> {
        const id = this.host.options.permissionModes?.[mode];
        const selector = this.config("mode");
        if (id && selector) {
            await this.setConfig(selector, id);
            return;
        }
        if (!id || !this.sessionId || !this.modes || this.modes.currentModeId === id) return;
        if (!this.modes.availableModes.some(candidate => candidate.id === id))
            throw new Error("ACP mode " + id + " is unavailable.");
        await this.connection?.setSessionMode({ sessionId: this.sessionId, modeId: id });
        this.modes.currentModeId = id;
    }

    async close(): Promise<void> {
        try {
            if (this.canCloseSession && this.connection && this.sessionId)
                await withTimeout(this.connection.closeSession({ sessionId: this.sessionId }), 750, "ACP session did not close.");
        } catch { /* The process is stopped even if the agent cannot close the session. */ }
        finally {
            await this.process?.stop();
        }
    }

    private async permission(params: RequestPermissionRequest): Promise<RequestPermissionResponse> {
        const turn = this.active;
        if (!turn || params.sessionId !== this.sessionId || turn.signal.aborted)
            return { outcome: { outcome: "cancelled" } };

        const tool = { ...this.tools.get(params.toolCall.toolCallId), ...params.toolCall };
        const name = tool.name ?? tool.title ?? "ACP tool";
        const decision = await turn.permission({
            toolName: name,
            mcpServerName: this.serverFor(tool.name),
            isReadOnly: tool.kind === "read" || tool.kind === "search" || tool.kind === "think",
            description: tool.title ?? undefined,
            options: params.options.map(option => ({ decision: decisions[option.kind], label: option.name })),
        });
        if (turn.signal.aborted)
            return { outcome: { outcome: "cancelled" } };

        const option = params.options.find(candidate => decisions[candidate.kind] === decision)
            ?? params.options.find(candidate => decision === "allowAlways" ? candidate.kind === "allow_once" : candidate.kind === "reject_once");
        return { outcome: option ? { outcome: "selected", optionId: option.optionId } : { outcome: "cancelled" } };
    }

    private serverFor(name: string | null | undefined): string | undefined {
        if (!name) return undefined;
        return this.host.options.mcpServers.find(server => name.startsWith("mcp__" + server.name + "__"))?.name;
    }

    private update({ sessionId, update }: SessionNotification): void {
        if (update.sessionUpdate === "available_commands_update") {
            const suggestions: ComposerSuggestion[] = update.availableCommands.map(command => ({
                name: command.name, description: command.description, argumentHint: command.input?.hint, kind: "command", source: "acp",
            }));
            if (sessionId === this.sessionId) this.setSuggestions(suggestions);
            else if (this.sessionId === undefined) this.earlySuggestions = { sessionId, suggestions };
            return;
        }
        if (sessionId !== this.sessionId) return;
        if (update.sessionUpdate === "config_option_update") {
            this.configOptions = update.configOptions;
            return;
        }
        if (update.sessionUpdate === "current_mode_update" && this.modes)
            this.modes.currentModeId = update.currentModeId;
        const turn = this.active;
        if (!turn) return;
        switch (update.sessionUpdate) {
            case "agent_message_chunk":
                if (update.content.type !== "text") break;
                if (this.messageId !== (update.messageId ?? undefined)) this.completeMessage();
                this.messageId = update.messageId ?? undefined;
                this.message += update.content.text;
                turn.emit({ type: "assistantTextDelta", text: update.content.text, messageId: this.messageId });
                break;
            case "agent_thought_chunk":
                if (update.content.type === "text")
                    turn.emit({ type: "reasoningDelta", text: update.content.text });
                break;
            case "tool_call": {
                this.completeMessage();
                this.tools.set(update.toolCallId, update);
                this.startTool(turn, update);
                this.updateTool(turn, update, update.status);
                break;
            }
            case "tool_call_update": {
                const previous = this.tools.get(update.toolCallId);
                const tool = { ...previous, ...update, _meta: { ...previous?._meta, ...update._meta } };
                this.tools.set(update.toolCallId, tool);
                if (isBackground(tool) && (!previous || !isBackground(previous))) this.startTool(turn, tool);
                this.updateTool(turn, tool, tool.status);
                break;
            }
            case "plan":
                turn.emit({ type: "planUpdated", steps: update.entries.map(entry => ({ text: entry.content, status: planStatus(entry.status) })) });
                break;
            case "notice":
                if (update.severity === "error" || update.severity === "warning")
                    turn.emit({ type: "errorNotice", message: [update.title, update.description].filter(Boolean).join("\n") });
                break;
        }
    }

    private startTool(turn: Turn, tool: ToolCallUpdate): void {
        const { toolCallId, name, title, rawInput } = tool;
        const background = isBackground(tool);
        const command = record(rawInput)?.command;
        turn.emit({ type: "toolCallStarted", toolCallId, toolName: name ?? title ?? "ACP tool", input: rawInput,
            mcpServerName: this.serverFor(name), parentToolCallId: parentOf(tool), isBackground: background,
            backgroundTitle: background ? (typeof command === "string" ? command : title ?? undefined) : undefined });
    }

    private updateTool(turn: Turn, tool: ToolCallUpdate, status: ToolCallStatus | null | undefined): void {
        const { toolCallId, title, content, rawOutput } = tool;
        const parentToolCallId = parentOf(tool);
        if (status === "completed" || status === "failed")
            turn.emit({ type: "toolCallCompleted", toolCallId, status, outputs: contentOutputs(content ?? rawOutput), parentToolCallId });
        else
            turn.emit({ type: "toolCallUpdated", toolCallId, status: status === "pending" ? status : "running", message: title ?? undefined, parentToolCallId });
    }

    private completeMessage(): void {
        if (this.message)
            this.active?.emit({ type: "assistantMessageCompleted", text: this.message, messageId: this.messageId });
        this.message = "";
        this.messageId = undefined;
    }
}

function parentOf(tool: ToolCallUpdate): string | undefined {
    const parent = tool._meta?.parentToolCallId ?? tool._meta?.parent_tool_call_id;
    return typeof parent === "string" ? parent : undefined;
}

function isBackground(tool: ToolCallUpdate): boolean {
    return tool._meta?.isBackground === true || tool._meta?.background === true;
}
