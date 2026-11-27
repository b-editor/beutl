import { query, type CanUseTool, type McpServerConfig, type Options, type Query, type SDKMessage, type SDKUserMessage } from "@anthropic-ai/claude-agent-sdk/core";
import { randomUUID } from "node:crypto";
import { isAbsolute, normalize } from "node:path";
import { ClaudeEvents, claudeTokenUsage, fallbackContextWindow } from "./claudeEvents.js";
import { ProviderProcess } from "./process.js";
import { type Adapter, type AdapterHost, type BackgroundStopResult, type CompactionResult, type ComposerSuggestion, type ModelDescriptor, type PermissionMode, type Question, type Turn, type TurnRequest, type TurnResult, findMcpServerName, formatPrompt, isAllowed, record, withTimeout } from "./protocol.js";

const readTools = new Set(["Read", "Glob", "Grep", "LS"]);

function sameExecutable(a: string, b: string): boolean {
    const left = normalize(a);
    const right = normalize(b);
    return process.platform === "win32" ? left.toLowerCase() === right.toLowerCase() : left === right;
}

export class ClaudeAdapter implements Adapter {
    private sessionId?: string;
    private process?: ProviderProcess;
    private active?: Query;
    private input?: ClaudeTurnInput;
    private events?: ClaudeEvents;
    private mode: PermissionMode;
    private skillNames = new Map<string, string>();
    private readonly contextWindows = new Map<string, number>();

    constructor(private readonly host: AdapterHost) {
        this.sessionId = host.options.externalSessionId;
        this.mode = host.options.permissionMode;
    }

    async open(): Promise<void> {
        const { executablePath } = this.host.options;
        if (!isAbsolute(executablePath) || sameExecutable(executablePath, process.execPath))
            throw new Error("Claude Code requires its own absolute CLI executable path.");
    }

    async listModels(providerId: string): Promise<ModelDescriptor[]> {
        return await this.discover(async messages => {
            const models = await messages.supportedModels();
            let index = 0;
            return models.map(model => {
                const isProviderDefault = model.value === "default";
                const displayName = /^(Opus|Sonnet|Fable|Haiku)\b/i.test(model.displayName)
                    ? "Claude " + model.displayName : model.displayName;
                return {
                    id: model.value,
                    providerId,
                    displayName,
                    shortName: displayName.replace(/^Claude\s+/i, "").replace(/\s+\([^)]*\)$/, ""),
                    reasoningEfforts: model.supportedEffortLevels ?? [],
                    isSecondary: !isProviderDefault && index++ >= 4,
                    isProviderDefault,
                    resolvedModelId: model.resolvedModel,
                    contextWindowTokens: fallbackContextWindow(model.resolvedModel ?? model.value),
                };
            });
        }, "Claude Code did not report its models.");
    }

    async listSuggestions(): Promise<ComposerSuggestion[]> {
        return await this.discover(async messages => suggestions(await messages.supportedCommands()),
            "Claude Code did not report its capabilities.");
    }

    private async discover<T>(read: (messages: Query) => Promise<T>, timeoutMessage: string): Promise<T> {
        await this.open();
        const abortController = new AbortController();
        // Start the SDK's control channel without yielding a user message.
        async function* idlePrompt() {
            if (!abortController.signal.aborted)
                await new Promise<void>(resolve => abortController.signal.addEventListener("abort", () => resolve(), { once: true }));
        }
        const messages = this.active = query({ prompt: idlePrompt(), options: { ...this.baseOptions(), abortController } });
        try {
            return await withTimeout(read(messages), 5000, timeoutMessage);
        } finally {
            abortController.abort();
            messages.close();
            this.active = undefined;
        }
    }

    async run(turn: Turn): Promise<TurnResult> {
        return this.runCore(turn);
    }

    async compact(turn: Turn): Promise<CompactionResult> {
        if (!this.sessionId) return { status: "unsupported" };
        let compacted = false;
        const result = await this.runCore(turn, () => { compacted = true; });
        return result.status === "completed" && !compacted ? { status: "unsupported" } : result;
    }

    private async runCore(turn: Turn, onManualCompact?: () => void): Promise<TurnResult> {
        const input = this.input = new ClaudeTurnInput(turn);
        input.push(onManualCompact ? "/compact" : this.formatPrompt(turn.request));
        const closeInput = () => input.close();
        turn.signal.addEventListener("abort", closeInput, { once: true });
        const translate = this.events = new ClaudeEvents(turn, model => this.contextWindows.get(model ?? "") ?? fallbackContextWindow(model),
            onManualCompact);
        try {
            turn.signal.throwIfAborted();
            const messages = this.active = query({ prompt: input, options: this.options(turn) });
            for await (const message of messages) {
                if (message.type === "system" && message.subtype === "init") {
                    this.saveSession(message.session_id);
                    translate.model ??= message.model;
                    turn.reportUsage({ model: message.model });
                    const commands = await withTimeout(messages.supportedCommands(), 2000,
                        "Claude Code did not report its commands.").catch(() => []);
                    this.publishSuggestions(suggestions(commands, message.slash_commands, message.skills));
                } else if (message.type === "system" && message.subtype === "commands_changed") {
                    this.publishSuggestions(suggestions(message.commands));
                }
                if (message.type === "result") {
                    const result = await this.handleResult(message, messages, turn, input, translate, onManualCompact !== undefined);
                    if (result) return result;
                    continue;
                }
                translate.accept(message);
            }
            return turn.signal.aborted ? { status: "cancelled" } : { status: "failed", error: "Claude Code ended without a result." };
        } finally {
            turn.signal.removeEventListener("abort", closeInput);
            input.close();
            this.input = undefined;
            this.events = undefined;
            this.active?.close();
            this.active = undefined;
            const child = this.process;
            this.process = undefined;
            if (child) {
                try {
                    await withTimeout(child.closed, 1500, "Claude Code did not exit after closing the query.");
                } catch {
                    await child.stop();
                }
            }
        }
    }

    private async handleResult(
        message: Extract<SDKMessage, { type: "result" }>,
        messages: Query,
        turn: Turn,
        input: ClaudeTurnInput,
        translate: ClaudeEvents,
        isManualCompact: boolean,
    ): Promise<TurnResult | undefined> {
        this.saveSession(message.session_id);
        if (turn.signal.aborted) return { status: "cancelled" };
        if (!input.includesAllSteers(message)) return undefined;
        const models = Object.entries(message.modelUsage ?? {});
        for (const [model, usage] of models) {
            if (usage.contextWindow > 0) this.contextWindows.set(model, usage.contextWindow);
        }
        if (message.subtype === "success" && !message.is_error) {
            const model = translate.model ?? models[0]?.[0];
            const totals = claudeTokenUsage(message.usage);
            const modelUsage = models.find(([key]) => key === model)?.[1]
                ?? (models.length === 1 ? models[0][1] : undefined);
            const window = this.contextWindows.get(model ?? "") ?? fallbackContextWindow(model);
            if (turn.contextUsage) {
                const usage = turn.contextUsage;
                // A compaction boundary reports the old window. Ask the session for the new size.
                const current = usage.usedTokens === undefined && typeof messages.getContextUsage === "function"
                    ? await withTimeout(messages.getContextUsage({ detail: "summary" }), 1800,
                        "Claude Code did not report context usage.").catch(() => undefined)
                    : undefined;
                turn.reportContextUsage(current
                    ? { ...claudeTokenUsage(current.apiUsage ?? undefined), usedTokens: current.totalTokens,
                        contextWindowTokens: current.maxTokens > 0 ? current.maxTokens : window }
                    : { ...usage, contextWindowTokens: window });
            } else if (!isManualCompact && (message.num_turns === 1 || message.num_turns === undefined)) {
                const counts = modelUsage ? claudeTokenUsage({
                    input_tokens: modelUsage.inputTokens, output_tokens: modelUsage.outputTokens,
                    cache_read_input_tokens: modelUsage.cacheReadInputTokens,
                    cache_creation_input_tokens: modelUsage.cacheCreationInputTokens,
                }) : totals;
                if (counts.inputTokens !== undefined)
                    turn.reportContextUsage({ ...counts, usedTokens: counts.inputTokens + (counts.outputTokens ?? 0),
                        contextWindowTokens: window });
            }
            turn.reportUsage({ ...totals, model });
            turn.durationMs = message.duration_ms;
            return { status: "completed" };
        }
        const error = message.subtype === "success" ? message.result : message.errors.join("\n");
        return { status: "failed", error: error || message.subtype };
    }

    async steer(turn: Turn, request: TurnRequest): Promise<boolean> {
        return this.input?.turn === turn && this.input.push(this.formatPrompt(request), true);
    }

    async stopBackgroundTask(toolCallId: string): Promise<BackgroundStopResult> {
        const query = this.active;
        if (!query || !this.events) return "unavailable";
        if (typeof query.stopTask !== "function") return "unsupported";
        const taskId = this.events.runningTaskId(toolCallId);
        if (!taskId) return "unavailable";
        await withTimeout(query.stopTask(taskId), 5000, "Claude Code did not acknowledge the stop request.");
        return "requested";
    }

    private formatPrompt(request: TurnRequest): string {
        // Claude uses slash syntax for native skills. Keep ordinary text such as "$100" intact.
        return formatPrompt(request).replace(/^\$([\p{L}\p{N}_.:-]+)(?=\s|$)/u, (token, name: string) => {
            const skillName = request.providerSkills?.find(skill => skill.toLowerCase() === name.toLowerCase())
                ?? this.skillNames.get(name.toLowerCase());
            return skillName ? `/${skillName}` : token;
        });
    }

    private publishSuggestions(entries: ComposerSuggestion[]): void {
        this.skillNames = new Map(entries.filter(entry => entry.kind === "skill").map(entry => [entry.name.toLowerCase(), entry.name]));
        this.host.publishSuggestions(entries);
    }

    async cancel(): Promise<void> {
        await this.active?.interrupt();
    }

    async close(): Promise<void> {
        this.input?.close();
        this.active?.close();
        await this.process?.stop();
    }

    async setMode(mode: PermissionMode): Promise<void> {
        this.mode = mode;
    }

    private saveSession(id: string): void {
        if (id && id !== this.sessionId) {
            this.sessionId = id;
            this.host.saveSessionId(id);
        }
    }

    private options(turn: Turn): Options {
        const { mcpServers, model, reasoningEffort } = this.host.options;
        const servers = Object.fromEntries(mcpServers.map((server): [string, McpServerConfig] => [
            server.name, server.type === "http"
                ? { type: "http", url: server.url, headers: server.headers }
                : { type: "stdio", command: server.command, args: server.args, env: server.env },
        ]));

        const effort: Options["effort"] = reasoningEffort === "low" || reasoningEffort === "medium"
            || reasoningEffort === "high" || reasoningEffort === "xhigh" || reasoningEffort === "max" ? reasoningEffort : undefined;
        const permissionMode: Options["permissionMode"] = this.mode === "fullAccess" ? "bypassPermissions"
            : this.mode === "auto" ? "auto"
            : this.mode === "autoAcceptEdits" ? "acceptEdits"
            : "default";
        return {
            ...this.baseOptions(),
            model,
            effort,
            resume: this.sessionId,
            includePartialMessages: true,
            forwardSubagentText: true,
            abortController: turn.controller,
            permissionMode,
            allowDangerouslySkipPermissions: this.mode === "fullAccess",
            mcpServers: servers,
            mcpServersDelivery: "initialize",
            canUseTool: this.permission(turn),
            hooks: {
                PreToolUse: [{ hooks: [async input => {
                    if (input.hook_event_name !== "PreToolUse") return {};
                    if (input.tool_name !== "AskUserQuestion" && (this.mode !== "supervised" || readTools.has(input.tool_name)))
                        return {};
                    return {
                        hookSpecificOutput: {
                            hookEventName: "PreToolUse",
                            permissionDecision: "ask",
                        },
                    };
                }] }],
            },
        };
    }

    private baseOptions(): Options {
        const { executablePath, workingDirectory, environment } = this.host.options;
        return {
            pathToClaudeCodeExecutable: executablePath,
            cwd: workingDirectory,
            env: { ...process.env, ...environment },
            settingSources: ["user", "project"],
            strictMcpConfig: true,
            spawnClaudeCodeProcess: ({ command, args, cwd, env, signal }) => {
                // Older SDKs may wrap a CLI path in the current JS executable. The embedded
                // process is a .NET apphost, so always launch the explicitly resolved CLI.
                const cliArgs = sameExecutable(command, executablePath) ? args
                    : args[0] && sameExecutable(args[0], executablePath) ? args.slice(1) : undefined;
                if (!cliArgs) throw new Error("Claude SDK attempted to launch an unexpected executable.");
                const child = this.process = new ProviderProcess(
                    this.host, executablePath, [...(this.host.options.arguments ?? []), ...cliArgs], cwd, env);
                const stop = () => { void child.stop(); };
                signal.addEventListener("abort", stop, { once: true });
                void child.closed.then(() => signal.removeEventListener("abort", stop));
                if (signal.aborted) stop();
                return child.child;
            },
        };
    }

    private permission(turn: Turn): CanUseTool {
        return async (name, input, options) => {
            if (options.signal.aborted || turn.signal.aborted)
                return { behavior: "deny", message: "The turn was cancelled." };

            if (name === "AskUserQuestion")
                return await this.handleAskUserQuestion(turn, input, options.toolUseID);

            const mcpServer = options.mcpServer;
            const mcpServerName = mcpServer
                ? (mcpServer.source === "dynamic" && this.host.options.mcpServers.some(server => server.name === mcpServer.name)
                    ? mcpServer.name : undefined)
                : findMcpServerName(this.host.options.mcpServers, name);
            const decision = await turn.permission({
                toolName: name,
                mcpServerName,
                isReadOnly: readTools.has(name),
                description: options.title ?? options.description ?? options.decisionReason,
                options: [
                    { decision: "allowOnce", label: "Allow once" },
                    ...(!options.suppressAlwaysAllowRule ? [{ decision: "allowAlways" as const, label: "Allow for session" }] : []),
                    { decision: "rejectOnce", label: "Reject once" },
                    { decision: "rejectAlways", label: "Reject for session" },
                ],
            });
            return isAllowed(decision)
                ? {
                    behavior: "allow", updatedInput: input, toolUseID: options.toolUseID,
                    updatedPermissions: decision === "allowAlways" && !options.suppressAlwaysAllowRule ? options.suggestions : undefined,
                }
                : { behavior: "deny", message: "Permission denied by Beutl.", toolUseID: options.toolUseID };
        };
    }

    private async handleAskUserQuestion(
        turn: Turn,
        input: Record<string, unknown>,
        toolUseID: string,
    ): Promise<Awaited<ReturnType<CanUseTool>>> {
        const questions = Array.isArray(input.questions) ? input.questions.flatMap((value, index): Question[] => {
            const item = record(value);
            if (typeof item?.question !== "string") return [];
            const options = Array.isArray(item.options) ? item.options.flatMap(value => {
                const option = record(value);
                return typeof option?.label === "string"
                    ? [{ label: option.label, description: typeof option.description === "string" ? option.description : undefined }] : [];
            }) : undefined;
            return [{ id: String(index), text: item.question, options,
                header: typeof item.header === "string" ? item.header : undefined, multiSelect: item.multiSelect === true }];
        }) : [];
        const answers = await turn.ask(questions);
        const updated = Object.fromEntries(questions.flatMap(question =>
            answers[question.id]?.length ? [[question.text, question.multiSelect
                ? answers[question.id].join(", ") : answers[question.id][0]]] : []));
        return Object.keys(updated).length
            ? { behavior: "allow", updatedInput: { ...input, answers: updated }, toolUseID }
            : { behavior: "deny", message: "No answer was provided.", toolUseID };
    }
}

class ClaudeTurnInput implements AsyncIterable<SDKUserMessage> {
    private readonly queued: SDKUserMessage[] = [];
    private readonly pendingSteers = new Set<string>();
    private available = Promise.withResolvers<void>();
    private closed = false;

    constructor(readonly turn: Turn) {}

    push(content: string, steer = false): boolean {
        if (this.closed) return false;
        const uuid = randomUUID();
        if (steer) this.pendingSteers.add(uuid);
        this.queued.push({
            type: "user", uuid, message: { role: "user", content }, parent_tool_use_id: null,
            ...(steer && { priority: "now", origin: { kind: "human" } }),
        });
        this.available.resolve();
        return true;
    }

    includesAllSteers(result: Extract<SDKMessage, { type: "result" }>): boolean {
        if (this.pendingSteers.size === 0) return true;
        const ids = result.user_message_uuids ?? (result.user_message_uuid ? [result.user_message_uuid] : undefined);
        // Older CLIs report a single result without input IDs.
        if (!ids) return true;
        for (const id of ids) this.pendingSteers.delete(id);
        return this.pendingSteers.size === 0;
    }

    async *[Symbol.asyncIterator](): AsyncGenerator<SDKUserMessage> {
        while (!this.closed) {
            const message = this.queued.shift();
            if (message) {
                yield message;
            } else {
                this.available = Promise.withResolvers<void>();
                await this.available.promise;
            }
        }
    }

    close(): void {
        this.closed = true;
        this.queued.length = 0;
        this.available.resolve();
    }
}

function suggestions(commands: Awaited<ReturnType<Query["supportedCommands"]>>, names: string[] = [], skills: string[] = []): ComposerSuggestion[] {
    const entries = new Map<string, ComposerSuggestion>();
    for (const name of names) entries.set(name, { name, kind: "command", source: "claude" });
    for (const name of skills) entries.set(name, { name, kind: "skill", source: "claude" });
    for (const { name, description, argumentHint, builtin, aliases } of commands ?? []) {
        for (const key of [name, ...(aliases ?? [])]) {
            const existing = entries.get(key);
            if (!key || (key !== name && existing?.description) || (!builtin && existing?.kind === "command" && existing.description)) continue;
            entries.set(key, { name: key, description, argumentHint, kind: builtin ? "command" : "skill", source: "claude" });
        }
    }
    return [...entries.values()];
}
