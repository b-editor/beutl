import { randomUUID } from "node:crypto";
import { isAbsolute } from "node:path";
import { AcpAdapter } from "./acp.js";
import { ClaudeAdapter } from "./claude.js";
import { CodexAdapter } from "./codex.js";
import { type Adapter, type AdapterHost, type BackgroundStopResult, type CompactionResult, type ComposerSuggestion, type ModelDescriptor, type ModelListOptions, type PermissionMode, type RequestHost, type SessionOptions, type SuggestionListOptions, type TimelineEvent, type TurnRequest, type TurnResult, Turn, errorMessage, withTimeout } from "./protocol.js";

type Command =
    | { method: "ping" | "shutdown" }
    | { method: "models/list"; params: ModelListOptions }
    | { method: "suggestions/list"; params: SuggestionListOptions }
    | { method: "session/open"; params: SessionOptions }
    | { method: "session/setMode"; params: { threadId: string; mode: PermissionMode } }
    | { method: "session/close"; params: { threadId: string } }
    | { method: "turn/run"; params: TurnRequest }
    | { method: "turn/compact"; params: Pick<TurnRequest, "threadId" | "turnId"> }
    | { method: "turn/steer"; params: TurnRequest }
    | { method: "turn/stopTask"; params: { threadId: string; turnId: string; toolCallId: string } }
    | { method: "turn/cancel"; params: { threadId: string; turnId: string } };

let emitEvent: (event: string) => void;
let requestHost: RequestHost;
let shuttingDown = false;
const sessions = new Map<string, Session>();

export function initialize(emitEventJson: (event: string) => void, requestHostJson: (request: string) => Promise<string>): void {
    emitEvent = emitEventJson;
    requestHost = async (method, params, signal) => {
        signal?.throwIfAborted();
        const id = randomUUID();
        const onAbort = () => {
            void requestHostJson(JSON.stringify({ method: "request/cancel", id })).catch(() => {});
        };
        signal?.addEventListener("abort", onAbort, { once: true });
        try {
            return JSON.parse(await requestHostJson(JSON.stringify({ id, method, params }))) as unknown;
        } finally {
            signal?.removeEventListener("abort", onAbort);
        }
    };
}

export async function handleCommand(commandJson: string): Promise<string> {
    const command = JSON.parse(commandJson) as Command;
    if (shuttingDown && command.method !== "shutdown") throw new Error("The agent bridge is shutting down.");
    switch (command.method) {
        case "ping":
            return JSON.stringify({ processId: process.pid, nodeVersion: process.versions.node, nodeExecutablePath: process.execPath });
        case "models/list":
            return JSON.stringify(await listModels(command.params));
        case "suggestions/list":
            return JSON.stringify(await listSuggestions(command.params));
        case "session/open": {
            const session = await openSession(command.params);
            return JSON.stringify({ externalSessionId: session.externalId });
        }
        case "session/close":
            await sessions.get(command.params.threadId)?.close();
            break;
        case "session/setMode": {
            const session = sessions.get(command.params.threadId);
            if (session) await session.setMode(command.params.mode);
            break;
        }
        case "turn/run": {
            const session = requireSession(command.params.threadId);
            return JSON.stringify({ turnId: command.params.turnId, ...await session.run(command.params) });
        }
        case "turn/compact": {
            const session = requireSession(command.params.threadId);
            return JSON.stringify(await session.run({ ...command.params, text: "/compact" }, true));
        }
        case "turn/steer":
            return JSON.stringify({ steered: await sessions.get(command.params.threadId)?.steer(command.params) ?? false });
        case "turn/stopTask":
            return JSON.stringify(await sessions.get(command.params.threadId)
                ?.stopBackgroundTask(command.params.turnId, command.params.toolCallId) ?? "unavailable");
        case "turn/cancel":
            await sessions.get(command.params.threadId)?.cancel(command.params.turnId);
            break;
        case "shutdown":
            shuttingDown = true;
            await Promise.allSettled([...sessions.values()].map(session => session.close()));
            break;
        default:
            throw new Error("Unknown agent bridge command.");
    }
    return "null";
}

function assertAbsolutePaths(executablePath: string, workingDirectory: string): void {
    if (!isAbsolute(executablePath) || !isAbsolute(workingDirectory))
        throw new Error("Agent executable and working directory must be absolute.");
}

function requireSession(threadId: string): Session {
    const session = sessions.get(threadId);
    if (!session) throw new Error("The agent session is closed.");
    return session;
}

async function terminateProcess(pid: number): Promise<void> {
    await requestHost("process/terminate", { pid });
}

async function withDiscoveryAdapter<T>(
    provider: Omit<SuggestionListOptions, "threadId">,
    timeoutMs: number,
    timeoutMessage: string,
    action: (adapter: Adapter) => T | Promise<T>,
): Promise<T> {
    const adapter = discoveryAdapter(provider);
    try {
        return await withTimeout((async () => {
            if (adapter instanceof AcpAdapter) await adapter.open();
            return await action(adapter);
        })(), timeoutMs, timeoutMessage);
    } finally {
        await adapter.close();
    }
}

async function listModels({ providerId, threadId, ...provider }: ModelListOptions): Promise<ModelDescriptor[]> {
    assertAbsolutePaths(provider.executablePath, provider.workingDirectory);

    const session = threadId ? sessions.get(threadId) : undefined;
    if (provider.provider === "acp" && session?.adapter instanceof AcpAdapter && !session.closing) {
        return await withTimeout(session.ready.then(() => session.adapter.listModels(providerId)), 6000,
            "The agent did not report its models.");
    }

    return await withDiscoveryAdapter(provider, 6000, "The agent did not report its models.",
        adapter => adapter.listModels(providerId));
}

async function listSuggestions({ threadId, ...provider }: SuggestionListOptions): Promise<ComposerSuggestion[]> {
    assertAbsolutePaths(provider.executablePath, provider.workingDirectory);

    const session = threadId ? sessions.get(threadId) : undefined;
    if (session && !session.closing && !(session.adapter instanceof ClaudeAdapter))
        return await withTimeout(session.ready.then(() => session.adapter.listSuggestions()), 6000,
            "The agent did not report its commands and skills.");

    return await withDiscoveryAdapter(provider, 6000, "The agent did not report its commands and skills.",
        adapter => adapter.listSuggestions());
}

function discoveryAdapter(provider: Omit<SuggestionListOptions, "threadId">) {
    const host: AdapterHost = {
        options: { ...provider, threadId: randomUUID(), permissionMode: "supervised", mcpServers: [] },
        saveSessionId: () => {},
        publishSuggestions: () => {},
        disconnected: () => {},
        terminateProcess,
    };
    return new Adapters[provider.provider](host);
}

const Adapters = { acp: AcpAdapter, codex: CodexAdapter, claudeCode: ClaudeAdapter };

async function openSession(options: SessionOptions): Promise<Session> {
    assertAbsolutePaths(options.executablePath, options.workingDirectory);
    const names = new Set(options.mcpServers.map(server => server.name));
    if (names.size !== options.mcpServers.length) throw new Error("MCP server names must be unique.");

    let existing: Session | undefined;
    while ((existing = sessions.get(options.threadId)) !== undefined) {
        if (!existing.closing && existing.matches(options)) {
            await existing.ready;
            await existing.configure(options);
            if (existing.closing) throw new Error("The agent session is closing.");
            return existing;
        }
        if (existing.provider === options.provider && existing.executablePath === options.executablePath)
            options = { ...options, externalSessionId: existing.externalId ?? options.externalSessionId };
        await existing.close();
    }
    if (shuttingDown) throw new Error("The agent bridge is shutting down.");

    const session = new Session(options);
    sessions.set(options.threadId, session);
    try {
        await session.ready;
        return session;
    } catch (error) {
        await session.close();
        throw error;
    }
}

interface ActiveTurn {
    turn: Turn;
    stopped: PromiseWithResolvers<TurnResult>;
    running?: Promise<CompactionResult>;
    cancellation?: Promise<void>;
    steering?: Promise<boolean>;
    finishing?: boolean;
}

class Session {
    readonly ready: Promise<void>;
    readonly adapter: Adapter;
    externalId?: string;
    closing?: Promise<void>;
    private opened = false;
    private active?: ActiveTurn;

    constructor(private readonly options: SessionOptions) {
        this.externalId = options.externalSessionId;
        const host: AdapterHost = {
            options,
            saveSessionId: (id: string) => {
                const changed = id !== this.externalId;
                this.externalId = id;
                if (this.opened && changed) this.emit({ type: "sessionStateChanged", state: "open", externalSessionId: id });
            },
            publishSuggestions: (suggestions: ComposerSuggestion[]) => {
                if (!this.closing) this.emit({ type: "composerSuggestionsChanged", suggestions });
            },
            terminateProcess,
            disconnected: (message: string) => {
                if (!this.opened || this.closing) return;
                this.emit({ type: "errorNotice", message }, this.active?.turn.request.turnId);
                this.active?.stopped.resolve({ status: "failed", error: message });
                void this.close().catch(error => console.error(errorMessage(error)));
            },
        };
        const Factory = Adapters[options.provider];
        if (!Factory) throw new Error("Unknown agent provider.");
        this.adapter = new Factory(host);
        this.ready = this.open();
    }

    get provider() { return this.options.provider; }
    get executablePath() { return this.options.executablePath; }

    matches(options: SessionOptions): boolean {
        const config = ({ externalSessionId, permissionMode, model, reasoningEffort, ...rest }: SessionOptions) =>
            JSON.stringify({
                ...rest,
                permissionMode: rest.provider === "codex" ? permissionMode : undefined,
                model: rest.provider === "acp" ? undefined : model,
                reasoningEffort: rest.provider === "acp" ? undefined : reasoningEffort,
            });
        return config(this.options) === config(options);
    }

    async configure(options: SessionOptions): Promise<void> {
        await this.setMode(options.permissionMode);
        if (this.adapter instanceof AcpAdapter)
            await this.adapter.setModel(options.model, options.reasoningEffort);
    }

    async setMode(mode: PermissionMode): Promise<void> {
        await this.ready;
        if (this.active) throw new Error("Cannot change permissions while an agent turn is running.");
        await this.adapter.setMode(mode);
    }

    private async open(): Promise<void> {
        await withTimeout((async () => {
            await this.adapter.open();
            if (this.adapter instanceof AcpAdapter)
                await this.adapter.setModel(this.options.model, this.options.reasoningEffort);
        })(), 15_000, "The agent did not initialize.");
        if (this.closing) throw new Error("The agent session was closed during initialization.");
        this.opened = true;
        this.emit({ type: "sessionStateChanged", state: "open", externalSessionId: this.externalId });
    }

    async run(request: TurnRequest, compact = false): Promise<CompactionResult> {
        await this.ready;
        if (this.closing) throw new Error("The agent session is closing.");
        if (this.active) throw new Error("An agent turn is already running.");
        const turn = new Turn(request, event => {
            if (this.active?.turn === turn && (!compact || event.type === "contextUsageUpdated"))
                this.emit(event, request.turnId);
        }, requestHost);
        const active: ActiveTurn = this.active = { turn, stopped: Promise.withResolvers<TurnResult>() };
        if (!compact) {
            turn.emit({ type: "turnStarted" });
            turn.recordPrompt(request);
        }

        let result: CompactionResult;
        try {
            active.running = compact
                ? this.adapter.compact?.(turn) ?? Promise.resolve({ status: "unsupported" })
                : this.adapter.run(turn);
            result = await Promise.race([active.running, active.stopped.promise]);
        } catch (error) {
            result = turn.signal.aborted ? { status: "cancelled" } : { status: "failed", error: errorMessage(error) };
        }
        active.finishing = true;
        await active.steering?.catch(() => {});
        if (active.cancellation) result = { status: "cancelled" };

        if (!compact) {
            switch (result.status) {
                case "completed": turn.emit({ type: "turnCompleted", durationMs: turn.durationMs,
                    contextUsage: turn.contextUsage, ...turn.usage }); break;
                case "failed": turn.emit({ type: "turnFailed", error: result.error }); break;
                case "cancelled": turn.emit({ type: "turnCancelled" }); break;
            }
        }
        turn.controller.abort();
        this.active = undefined;
        return result;
    }

    async steer(request: TurnRequest): Promise<boolean> {
        const active = this.active;
        if (!active || active.turn.request.turnId !== request.turnId || active.turn.signal.aborted || active.finishing)
            return false;
        if (active.steering) throw new Error("A steer request is already in progress.");
        active.steering = this.steerCore(active.turn, request);
        try {
            return await active.steering;
        } finally {
            active.steering = undefined;
        }
    }

    private async steerCore(turn: Turn, request: TurnRequest): Promise<boolean> {
        if (!await this.adapter.steer(turn, request)) return false;
        turn.recordPrompt(request);
        return true;
    }

    async cancel(turnId: string): Promise<void> {
        const active = this.active;
        if (!active || active.turn.request.turnId !== turnId) return;
        active.cancellation ??= this.cancelCore(active);
        await active.cancellation;
    }

    async stopBackgroundTask(turnId: string, toolCallId: string): Promise<BackgroundStopResult> {
        const active = this.active;
        if (!active || active.turn.request.turnId !== turnId || active.turn.signal.aborted || active.finishing)
            return "unavailable";
        return await this.adapter.stopBackgroundTask?.(toolCallId) ?? "unsupported";
    }

    private async cancelCore(active: ActiveTurn): Promise<void> {
        active.turn.controller.abort();
        try {
            await withTimeout((async () => {
                await this.adapter.cancel();
                await active.running;
            })(), 900, "Agent cancellation timed out.");
        } catch (error) {
            console.error(errorMessage(error));
        }
        await this.close();
    }

    close(): Promise<void> {
        return this.closing ??= this.closeCore();
    }

    private async closeCore(): Promise<void> {
        const active = this.active;
        active?.turn.controller.abort();
        try {
            await this.adapter.close();
        } finally {
            active?.stopped.resolve({ status: "cancelled" });
            if (sessions.get(this.options.threadId) === this)
                sessions.delete(this.options.threadId);
            if (this.opened) this.emit({ type: "sessionStateChanged", state: "closed", externalSessionId: this.externalId });
        }
    }

    private emit(event: TimelineEvent, turnId?: string): void {
        emitEvent(JSON.stringify({
            ...event, threadId: this.options.threadId, turnId, timestamp: new Date().toISOString(),
        }));
    }
}
