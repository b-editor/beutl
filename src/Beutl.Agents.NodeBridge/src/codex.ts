import { CodexRpc, CodexRpcError, type CodexNotification, type CodexRequest, type CodexInput, type CodexItem, type CodexSkill, type CodexTokenCounts, type CodexTurn } from "./codexProtocol.js";
import { ProviderProcess } from "./process.js";
import { type Adapter, type AdapterHost, type CompactionResult, type ComposerSuggestion, type ContextUsage, type Decision, type ModelDescriptor, type Permission, type PermissionMode, type ToolStatus, type Turn, type TurnRequest, type TurnResult, contentOutputs, formatPrompt, planStatus, withTimeout } from "./protocol.js";

function tomlString(value: string): string {
    return JSON.stringify(value).replace(/\x7f/g, "\\u007f");
}

function tomlKey(value: string): string {
    return /^[A-Za-z0-9_-]+$/.test(value) ? value : tomlString(value);
}

function tomlTable(values: Record<string, string>): string {
    return "{ " + Object.entries(values).map(([key, value]) => tomlKey(key) + " = " + tomlString(value)).join(", ") + " }";
}

function approval(decision: Decision, choices?: unknown[] | null): "accept" | "acceptForSession" | "decline" | "cancel" {
    let answer: ReturnType<typeof approval> = decision === "allowAlways" ? "acceptForSession" : decision === "allowOnce" ? "accept" : "decline";
    if (choices && !choices.includes(answer))
        answer = answer === "acceptForSession" && choices.includes("accept") ? "accept" : "cancel";
    return answer;
}

function approvalOptions(choices?: unknown[] | null): Permission["options"] {
    if (!choices) return undefined;
    const options: NonNullable<Permission["options"]> = [];
    if (choices.includes("accept")) options.push({ decision: "allowOnce", label: "Accept" });
    if (choices.includes("acceptForSession")) options.push({ decision: "allowAlways", label: "Accept for session" });
    options.push({ decision: "rejectOnce", label: "Decline" });
    return options;
}

interface ActiveTurn {
    turn: Turn;
    started: Promise<{ turn: CodexTurn } | undefined>;
    finished: PromiseWithResolvers<TurnResult>;
    externalId?: string;
    childThreads: Map<string, string>;
    collabParents: Map<string, string | undefined>;
    startedCollab: Set<string>;
    usageBaseline?: CodexTokenCounts;
    hasCompactionItems?: boolean;
    usageAtCompactionStart?: ContextUsage;
}

function tokenDifference(total: CodexTokenCounts, baseline: CodexTokenCounts): CodexTokenCounts {
    return {
        inputTokens: Math.max(0, total.inputTokens - baseline.inputTokens),
        outputTokens: Math.max(0, total.outputTokens - baseline.outputTokens),
        cachedInputTokens: Math.max(0, total.cachedInputTokens - baseline.cachedInputTokens),
    };
}

function toolStatus(status: string): ToolStatus {
    if (status === "completed") return "completed";
    if (status === "interrupted" || status === "shutdown") return "cancelled";
    if (status === "inProgress" || status === "running" || status === "pendingInit") return "running";
    return "failed";
}

export class CodexAdapter implements Adapter {
    private process?: ProviderProcess;
    private rpc?: CodexRpc;
    private threadId?: string;
    private model?: string;
    private tokenTotal?: CodexTokenCounts;
    private contextWindow?: number;
    private active?: ActiveTurn;
    private skills?: Promise<CodexSkill[]>;
    private mode: PermissionMode;

    constructor(private readonly host: AdapterHost) {
        this.mode = host.options.permissionMode;
    }

    async open(): Promise<void> {
        const options = this.host.options;
        const rpc = await this.startServer();
        const policy = this.policy();
        const params = { cwd: options.workingDirectory, model: options.model, sandbox: policy.sandbox, approvalPolicy: policy.approvalPolicy };
        const response = options.externalSessionId
            ? await rpc.request("thread/resume", { ...params, threadId: options.externalSessionId, excludeTurns: true })
            : await rpc.request("thread/start", params);
        this.threadId = response.thread.id;
        this.model = response.model ?? options.model;
        this.host.saveSessionId(this.threadId);
    }

    async listModels(providerId: string): Promise<ModelDescriptor[]> {
        const rpc = await this.startServer();
        const models: ModelDescriptor[] = [];
        const seen = new Set<string>();
        let cursor: string | undefined;
        for (let page = 0; page < 10; page++) {
            const response = await rpc.request("model/list", { cursor, includeHidden: false });
            for (const model of response.data) {
                const id = model.model || model.id;
                if (!id || seen.has(id)) continue;
                seen.add(id);
                const displayName = (model.displayName || id).replace(/^(GPT[- ][\d.]+)-([A-Z])/i, "$1 $2");
                models.push({
                    id, providerId, displayName,
                    shortName: displayName.replace(/^GPT[- ]/i, ""),
                    reasoningEfforts: (model.supportedReasoningEfforts ?? []).map(option => option.reasoningEffort),
                    defaultReasoningEffort: model.defaultReasoningEffort,
                    isSecondary: models.length >= 4,
                    isProviderDefault: model.isDefault,
                    contextWindowTokens: model.contextWindow ?? undefined,
                });
            }
            if (!response.nextCursor || response.nextCursor === cursor || models.length >= 100)
                break;
            cursor = response.nextCursor;
        }
        return models;
    }

    async listSuggestions(): Promise<ComposerSuggestion[]> {
        const rpc = this.rpc ?? await this.startServer();
        return (await this.listSkills(rpc)).map(skill => ({
            name: skill.name, description: skill.shortDescription || skill.description, kind: "skill", source: skill.scope || "codex",
        }));
    }

    private listSkills(rpc: CodexRpc): Promise<CodexSkill[]> {
        return this.skills ??= withTimeout(rpc.request("skills/list", { cwds: [this.host.options.workingDirectory] }),
            3000, "Codex did not report its skills.").then(response =>
            response.data.flatMap(entry => entry.skills).filter(skill => skill.enabled !== false)).catch(() => []);
    }

    private async createInput(turn: Turn, rpc: CodexRpc): Promise<CodexInput[]> {
        const input: CodexInput[] = [{ type: "text", text: turn.prompt, text_elements: [] }];
        const names = new Set(Array.from(turn.request.text.matchAll(/(?:^|\s)[/$]([\p{L}\p{N}_.:-]+)(?=\s|$)/gu),
            match => match[1].toLowerCase()));
        for (const skill of turn.request.bundledSkills ?? []) names.delete(skill.name.toLowerCase());
        if (names.size) {
            for (const skill of await this.listSkills(rpc)) {
                if (skill.path && names.delete(skill.name.toLowerCase()))
                    input.push({ type: "skill", name: skill.name, path: skill.path });
            }
        }
        return input;
    }

    private async startServer(): Promise<CodexRpc> {
        const options = this.host.options;
        const env = { ...process.env, ...options.environment };
        const args = [...(options.arguments ?? []), "app-server", "--listen", "stdio://"];
        for (const [index, server] of options.mcpServers.entries()) {
            const approvalMode = this.mode === "fullAccess" || this.mode === "auto" ? "approve" : "prompt";
            const fields = ["default_tools_approval_mode = " + tomlString(approvalMode)];
            if (server.type === "stdio") {
                fields.push("command = " + tomlString(server.command), "args = [" + (server.args ?? []).map(tomlString).join(", ") + "]");
                if (server.env) fields.push("env = " + tomlTable(server.env));
            } else {
                fields.push("url = " + tomlString(server.url));
                const headers = Object.fromEntries(Object.entries(server.headers ?? {}).map(([name, value], header) => {
                    const variable = "BEUTL_AGENT_MCP_HEADER_" + index + "_" + header;
                    env[variable] = value;
                    return [name, variable];
                }));
                if (Object.keys(headers).length) fields.push("env_http_headers = " + tomlTable(headers));
            }
            args.push("-c", "mcp_servers." + tomlKey(server.name) + "={ " + fields.join(", ") + " }");
        }
        const child = this.process = new ProviderProcess(this.host, options.executablePath, args, options.workingDirectory, env);
        const rpc = this.rpc = new CodexRpc(child, event => this.notify(event), request => this.request(request), message => {
            this.active?.finished.reject(new Error(message));
            this.host.disconnected(message);
        });
        await rpc.request("initialize", {
            clientInfo: { name: "beutl", title: "Beutl", version: "1" },
            capabilities: { experimentalApi: true, requestAttestation: false },
        });
        rpc.initialized();
        return rpc;
    }

    async run(turn: Turn): Promise<TurnResult> {
        const { rpc, threadId } = this;
        if (!rpc || !threadId) throw new Error("Codex thread is closed.");
        const { approvalPolicy, sandboxPolicy } = this.policy();
        return await this.runCore(turn, async () => {
            const input = await this.createInput(turn, rpc);
            turn.signal.throwIfAborted();
            return rpc.request("turn/start", {
                threadId,
                cwd: this.host.options.workingDirectory,
                input,
                model: this.host.options.model,
                effort: this.host.options.reasoningEffort,
                approvalPolicy,
                sandboxPolicy,
            });
        });
    }

    async compact(turn: Turn): Promise<CompactionResult> {
        const { rpc, threadId } = this;
        if (!rpc || !threadId) throw new Error("Codex thread is closed.");
        try {
            return await this.runCore(turn, async () => {
                turn.signal.throwIfAborted();
                await rpc.request("thread/compact/start", { threadId });
                // The RPC acknowledges the command; turn notifications identify its execution.
                return undefined;
            });
        } catch (error) {
            if (error instanceof CodexRpcError && (error.code === -32601
                || /\b(?:(?:unknown|unsupported|unrecognized) (?:method|variant)|method not found)\b/i.test(error.message)))
                return { status: "unsupported" };
            throw error;
        }
    }

    private async runCore(turn: Turn, start: () => Promise<{ turn: CodexTurn } | undefined>): Promise<TurnResult> {
        const active: ActiveTurn = this.active = { turn, started: Promise.resolve().then(start), finished: Promise.withResolvers<TurnResult>(),
            childThreads: new Map(), collabParents: new Map(), startedCollab: new Set() };
        try {
            const identify = active.started.then(response => { active.externalId ??= response?.turn.id; });
            const [, result] = await Promise.all([identify, active.finished.promise]);
            return result;
        } finally {
            this.active = undefined;
        }
    }

    async steer(turn: Turn, request: TurnRequest): Promise<boolean> {
        const { rpc, threadId, active } = this;
        if (!rpc || !threadId || active?.turn !== turn) return false;
        try {
            const expectedTurnId = active.externalId ?? (await active.started)?.turn.id;
            if (!expectedTurnId || this.active !== active || turn.signal.aborted) return false;
            await withTimeout(rpc.request("turn/steer", {
                threadId, expectedTurnId, input: [{ type: "text", text: formatPrompt(request), text_elements: [] }],
            }), 4000, "Codex did not acknowledge the steer request.");
            return true;
        } catch (error) {
            if (this.active !== active || turn.signal.aborted) return false;
            throw error;
        }
    }

    async cancel(): Promise<void> {
        const active = this.active;
        if (!this.rpc || !this.threadId || !active) return;
        const turnId = active.externalId ?? (await active.started)?.turn.id ?? active.externalId;
        if (turnId) await this.rpc.request("turn/interrupt", { threadId: this.threadId, turnId });
    }

    async close(): Promise<void> {
        await this.process?.stop();
    }

    async setMode(mode: PermissionMode): Promise<void> {
        this.mode = mode;
    }

    private policy() {
        if (this.mode === "fullAccess")
            return { approvalPolicy: "never", sandbox: "danger-full-access", sandboxPolicy: { type: "dangerFullAccess" } } as const;
        return {
            approvalPolicy: "on-request",
            sandbox: "workspace-write",
            sandboxPolicy: {
                type: "workspaceWrite",
                writableRoots: [],
                networkAccess: false,
                excludeTmpdirEnvVar: true,
                excludeSlashTmp: true,
            },
        } as const;
    }

    private async request({ method, params }: CodexRequest): Promise<unknown> {
        const active = this.active;
        const turn = active && params?.threadId === this.threadId
            && (!active.externalId || params.turnId === active.externalId) ? active.turn : undefined;
        switch (method) {
            case "item/commandExecution/requestApproval": {
                const decision = turn ? await turn.permission({
                    toolName: "Codex command", description: params.command ?? params.reason ?? undefined,
                    options: approvalOptions(params.availableDecisions),
                }) : "rejectOnce";
                return { decision: turn?.signal.aborted ? "cancel" : approval(decision, params.availableDecisions) };
            }
            case "item/fileChange/requestApproval": {
                if (this.mode === "autoAcceptEdits")
                    return { decision: "accept" };
                const decision = turn ? await turn.permission({ toolName: "Codex file change", description: params.reason ?? undefined }) : "rejectOnce";
                return { decision: turn?.signal.aborted ? "cancel" : approval(decision) };
            }
            case "item/permissions/requestApproval": {
                const decision = turn ? await turn.permission({ toolName: "Codex permissions", description: params.reason ?? undefined }) : "rejectOnce";
                const granted = !turn?.signal.aborted && (decision === "allowOnce" || decision === "allowAlways");
                return { permissions: granted ? params.permissions : {}, scope: granted && decision === "allowAlways" ? "session" : "turn" };
            }
            case "item/tool/requestUserInput": {
                const answers = turn ? await turn.ask(params.questions.map(question => ({
                    id: question.id, text: question.question, header: question.header, isSecret: question.isSecret,
                    isOther: question.isOther, options: question.options ?? undefined,
                }))) : {};
                return { answers: Object.fromEntries(Object.entries(answers).map(([id, answers]) => [id, { answers }])) };
            }
            default:
                throw new Error("Unsupported Codex app-server request: " + method);
        }
    }

    private notify(notification: CodexNotification): void {
        const active = this.active;
        const params = notification.params;
        if (!params?.threadId) return;
        const root = params.threadId === this.threadId;
        if (root && notification.method === "thread/tokenUsage/updated") {
            const { tokenUsage: { total, last, modelContextWindow }, turnId } = notification.params;
            const previous = this.tokenTotal;
            this.tokenTotal = total;
            this.contextWindow = modelContextWindow ?? this.contextWindow;
            // On resume, Codex may replay an older turn's cumulative usage.
            if (active?.externalId === turnId) {
                const baseline = active.usageBaseline ??= previous ?? tokenDifference(total, last);
                active.turn.reportUsage(tokenDifference(total, baseline));
                // The cumulative totals bill all model calls. Only the last call fills the current window.
                active.turn.reportContextUsage({
                    usedTokens: last.totalTokens ?? last.inputTokens + last.outputTokens,
                    contextWindowTokens: this.contextWindow, inputTokens: last.inputTokens,
                    outputTokens: last.outputTokens, cachedInputTokens: last.cachedInputTokens,
                });
            }
            return;
        }
        if (!active) return;
        const parentToolCallId = ("parentToolCallId" in params ? params.parentToolCallId : undefined)
            ?? ("item" in params ? params.item.parentToolCallId : undefined) ?? active.childThreads.get(params.threadId);
        if (!root && !parentToolCallId) return;
        const { turn } = active;
        if (root && "turnId" in params && active.externalId && params.turnId !== active.externalId) return;

        switch (notification.method) {
            case "turn/started":
                if (root) {
                    active.externalId = notification.params.turn.id;
                    turn.reportUsage({ model: this.host.options.model ?? this.model });
                }
                break;
            case "turn/completed": {
                const { id, status, error, durationMs } = notification.params.turn;
                if (!root) {
                    if (parentToolCallId) this.updateAgent(active, parentToolCallId, status, error?.message);
                    break;
                }
                if (active.externalId && id !== active.externalId) break;
                turn.durationMs = durationMs ?? undefined;
                if (!turn.usage.model) turn.reportUsage({ model: this.host.options.model ?? this.model });
                active.finished.resolve(status === "interrupted" ? { status: "cancelled" }
                    : status === "completed" ? { status }
                    : { status: "failed", error: error?.message ?? "Codex turn failed." });
                break;
            }
            case "item/agentMessage/delta":
                turn.emit({ type: "assistantTextDelta", text: notification.params.delta, messageId: notification.params.itemId, parentToolCallId });
                break;
            case "item/reasoning/textDelta":
            case "item/reasoning/summaryTextDelta":
                turn.emit({ type: "reasoningDelta", text: notification.params.delta, parentToolCallId });
                break;
            case "item/started":
            case "item/updated":
            case "item/completed":
                this.item(active, notification.params.item, notification.method === "item/completed", parentToolCallId);
                break;
            case "item/fileChange/outputDelta":
            case "item/commandExecution/outputDelta":
                turn.emit({ type: "toolCallUpdated", toolCallId: notification.params.itemId, status: "running", message: notification.params.delta, parentToolCallId });
                break;
            case "item/mcpToolCall/progress":
                turn.emit({ type: "toolCallUpdated", toolCallId: notification.params.itemId, status: "running", message: notification.params.message, parentToolCallId });
                break;
            case "turn/plan/updated":
                if (!root || parentToolCallId) break;
                turn.emit({
                    type: "planUpdated", steps: notification.params.plan.map(step => ({ text: step.step, status: planStatus(step.status) })),
                    explanation: notification.params.explanation ?? undefined,
                });
                break;
            case "thread/compacted":
                if (root && !active.hasCompactionItems) {
                    if (!turn.contextUsage) turn.reportContextUsage({ contextWindowTokens: this.contextWindow });
                    turn.emit({ type: "contextCompacted" });
                    turn.emit({ type: "turnPhaseChanged", phase: "thinking" });
                }
                break;
            case "model/rerouted":
                if (root) turn.reportUsage({ model: notification.params.toModel });
                break;
            case "error":
                if (root && !parentToolCallId) {
                    if (notification.params.willRetry)
                        turn.emit({ type: "turnPhaseChanged", phase: "retrying" });
                    else
                        turn.emit({ type: "errorNotice", message: notification.params.error.message });
                }
                break;
        }
    }

    private item(active: ActiveTurn, item: CodexItem, finished: boolean, parentToolCallId?: string): void {
        const { turn } = active;
        const start = (toolName: string, input?: unknown, mcpServerName?: string) =>
            turn.emit({ type: "toolCallStarted", toolCallId: item.id, toolName, input, mcpServerName, parentToolCallId });
        const complete = (status: string, outputs?: ReturnType<typeof contentOutputs>) =>
            turn.emit({ type: "toolCallCompleted", toolCallId: item.id, status: toolStatus(status), outputs, parentToolCallId });
        switch (item.type) {
            case "contextCompaction":
                if (!parentToolCallId) {
                    if (!finished && !active.hasCompactionItems) active.usageAtCompactionStart = turn.contextUsage;
                    if (finished) {
                        if (turn.contextUsage === active.usageAtCompactionStart)
                            turn.reportContextUsage({ contextWindowTokens: this.contextWindow });
                        turn.emit({ type: "contextCompacted" });
                    }
                    active.hasCompactionItems = true;
                    turn.emit({ type: "turnPhaseChanged", phase: finished ? "thinking" : "compacting" });
                }
                break;
            case "agentMessage":
                if (finished) turn.emit({ type: "assistantMessageCompleted", text: item.text, messageId: item.id, parentToolCallId });
                break;
            case "commandExecution": {
                const isBackground = item.isBackground === true || item.background === true;
                if (!finished || isBackground)
                    turn.emit({ type: "toolCallStarted", toolCallId: item.id, toolName: "Codex command", input: item.command,
                        parentToolCallId, isBackground, backgroundTitle: isBackground ? item.command : undefined });
                if (finished) complete(item.status, contentOutputs(item.aggregatedOutput));
                break;
            }
            case "collabAgentToolCall":
                this.collabItem(active, item, finished, parentToolCallId ?? (item.senderThreadId ? active.childThreads.get(item.senderThreadId) : undefined));
                break;
            case "fileChange":
                if (finished) complete(item.status, item.changes.map(change => ({ type: "artifact", uri: change.path })));
                else start("Codex file change", item.changes);
                break;
            case "mcpToolCall":
                if (finished) complete(item.status, contentOutputs(item.result?.content));
                else start(item.tool, item.arguments, item.server);
                break;
            case "dynamicToolCall":
                if (finished) complete(item.status, contentOutputs(item.contentItems));
                else start(item.tool, item.arguments);
                break;
            case "imageGeneration":
                if (finished) complete(item.status, item.savedPath ? [{ type: "image", uri: item.savedPath }]
                    : item.result ? [{ type: "image", data: item.result }] : undefined);
                else start("Image generation");
                break;
        }
    }

    private collabItem(active: ActiveTurn, item: Extract<CodexItem, { type: "collabAgentToolCall" }>, finished: boolean,
        parentToolCallId?: string): void {
        const receivers = item.receiverThreadIds ?? [];
        const spawn = item.tool === "spawnAgent";
        if (spawn) {
            for (const receiver of receivers) active.childThreads.set(receiver, item.id);
        } else if (receivers.length === 1) {
            parentToolCallId ??= active.childThreads.get(receivers[0]);
        }
        parentToolCallId ??= active.collabParents.get(item.id);
        active.collabParents.set(item.id, parentToolCallId);
        const { turn } = active;
        if (!active.startedCollab.has(item.id) || spawn && item.prompt) {
            active.startedCollab.add(item.id);
            turn.emit({ type: "toolCallStarted", toolCallId: item.id, toolName: item.tool, input: item.prompt ?? undefined, parentToolCallId,
                subagent: spawn ? { description: item.prompt ?? undefined, role: item.agentType ?? item.model ?? undefined } : undefined });
        }

        const states = Object.entries(item.agentsStates ?? {});
        const summary = states.length ? states.map(([id, state]) => `${id}: ${state.status}${state.message ? " — " + state.message : ""}`).join("\n")
            : receivers.join(", ") || undefined;
        if (spawn && finished && item.status === "completed" && receivers.length && !states.length) {
            this.updateAgent(active, item.id, "running", summary);
        } else if (spawn && states.length && item.status !== "failed" && item.status !== "interrupted") {
            const [state] = states.map(([, state]) => state);
            this.updateAgent(active, item.id, state.status, state.message ?? summary);
        } else {
            this.updateAgent(active, item.id, item.status, summary);
        }

        if (!spawn) {
            for (const [id, state] of states) {
                const parent = active.childThreads.get(id);
                if (parent && parent !== item.id) this.updateAgent(active, parent, state.status, state.message ?? undefined);
            }
        }
    }

    private updateAgent(active: ActiveTurn, toolCallId: string, status: string, message?: string): void {
        const parentToolCallId = active.collabParents.get(toolCallId);
        const normalized = toolStatus(status);
        if (normalized === "running")
            active.turn.emit({ type: "toolCallUpdated", toolCallId, status: normalized, parentToolCallId, message });
        else
            active.turn.emit({ type: "toolCallCompleted", toolCallId, status: normalized, parentToolCallId, outputs: contentOutputs(message) });
    }
}
