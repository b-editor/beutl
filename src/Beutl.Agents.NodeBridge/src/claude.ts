import { query, type CanUseTool, type McpServerConfig, type Options, type Query, type SDKMessage, type SDKUserMessage } from "@anthropic-ai/claude-agent-sdk/core";
import { randomUUID } from "node:crypto";
import { isAbsolute, normalize } from "node:path";
import { ProviderProcess } from "./process.js";
import { type Adapter, type AdapterHost, type BackgroundStopResult, type ComposerSuggestion, type ModelDescriptor, type PermissionMode, type Question, type Turn, type TurnRequest, type TurnResult, contentOutputs, formatPrompt, planStatus, record, withTimeout } from "./protocol.js";

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
    private mode: PermissionMode;
    private skillNames = new Map<string, string>();

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
            const models = await withTimeout(messages.supportedModels(), 5000, "Claude Code did not report its models.");
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
                };
            });
        });
    }

    async listSuggestions(): Promise<ComposerSuggestion[]> {
        return await this.discover(async messages => suggestions(await messages.supportedCommands()));
    }

    private async discover<T>(read: (messages: Query) => Promise<T>): Promise<T> {
        await this.open();
        const abortController = new AbortController();
        // Start the SDK's control channel without yielding a user message.
        async function* idlePrompt() {
            if (!abortController.signal.aborted)
                await new Promise<void>(resolve => abortController.signal.addEventListener("abort", () => resolve(), { once: true }));
        }
        const messages = this.active = query({ prompt: idlePrompt(), options: { ...this.baseOptions(), abortController } });
        try {
            return await withTimeout(read(messages), 5000, "Claude Code did not report its capabilities.");
        } finally {
            abortController.abort();
            messages.close();
            this.active = undefined;
        }
    }

    async run(turn: Turn): Promise<TurnResult> {
        const input = this.input = new ClaudeTurnInput(turn);
        input.push(this.formatPrompt(turn.request));
        const closeInput = () => input.close();
        turn.signal.addEventListener("abort", closeInput, { once: true });
        const translate = this.events = new ClaudeEvents(turn);
        try {
            turn.signal.throwIfAborted();
            const messages = this.active = query({ prompt: input, options: this.options(turn) });
            for await (const message of messages) {
                if (message.type === "system" && message.subtype === "init") {
                    this.saveSession(message.session_id);
                    turn.reportUsage({ model: message.model });
                    this.publishSuggestions(suggestions(await messages.supportedCommands().catch(() => []),
                        message.slash_commands, message.skills));
                } else if (message.type === "system" && message.subtype === "commands_changed") {
                    this.publishSuggestions(suggestions(message.commands));
                }
                if (message.type === "result") {
                    this.saveSession(message.session_id);
                    if (turn.signal.aborted) return { status: "cancelled" };
                    if (!input.includesAllSteers(message)) continue;
                    if (message.subtype === "success" && !message.is_error) {
                        const usage = message.usage;
                        const cachedInputTokens = usage?.cache_read_input_tokens ?? undefined;
                        // Claude reports fresh input separately from cache reads and writes.
                        const inputTokens = usage?.input_tokens === undefined ? undefined
                            : usage.input_tokens + (cachedInputTokens ?? 0) + (usage.cache_creation_input_tokens ?? 0);
                        turn.reportUsage({
                            inputTokens, outputTokens: usage?.output_tokens, cachedInputTokens,
                            model: translate.model ?? Object.keys(message.modelUsage ?? {})[0],
                        });
                        turn.durationMs = message.duration_ms;
                        return { status: "completed" };
                    }
                    const error = message.subtype === "success" ? message.result : message.errors.join("\n");
                    return { status: "failed", error: error || message.subtype };
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

    async steer(turn: Turn, request: TurnRequest): Promise<boolean> {
        return this.input?.turn === turn && this.input.push(this.formatPrompt(request), true);
    }

    private events?: ClaudeEvents;

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

            if (name === "AskUserQuestion") {
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
                    ? { behavior: "allow", updatedInput: { ...input, answers: updated }, toolUseID: options.toolUseID }
                    : { behavior: "deny", message: "No answer was provided.", toolUseID: options.toolUseID };
            }

            const mcpServer = options.mcpServer;
            const injected = this.host.options.mcpServers.find(server => mcpServer
                ? mcpServer.source === "dynamic" && mcpServer.name === server.name
                : name.startsWith("mcp__" + server.name + "__"));
            const decision = await turn.permission({
                toolName: name,
                mcpServerName: injected?.name,
                isReadOnly: readTools.has(name),
                description: options.title ?? options.description ?? options.decisionReason,
                options: [
                    { decision: "allowOnce", label: "Allow once" },
                    ...(!options.suppressAlwaysAllowRule ? [{ decision: "allowAlways" as const, label: "Allow for session" }] : []),
                    { decision: "rejectOnce", label: "Reject once" },
                    { decision: "rejectAlways", label: "Reject for session" },
                ],
            });
            return decision === "allowOnce" || decision === "allowAlways"
                ? {
                    behavior: "allow", updatedInput: input, toolUseID: options.toolUseID,
                    updatedPermissions: decision === "allowAlways" && !options.suppressAlwaysAllowRule ? options.suggestions : undefined,
                }
                : { behavior: "deny", message: "Permission denied by Beutl.", toolUseID: options.toolUseID };
        };
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

interface StreamedTextBlock {
    readonly streamIndex?: number;
    readonly messageId: string;
    text: string;
    completed: boolean;
}

class ClaudeEvents {
    model?: string;
    private readonly messageIds = new Map<string | null, string>();
    private readonly textBlocks = new Map<string, StreamedTextBlock[]>();
    private readonly calls = new Map<string, { parentToolCallId?: string; background: boolean; asynchronous: boolean }>();
    private readonly describedCalls = new Set<string>();
    private readonly tasks = new Map<string, string>();
    private readonly finishedTasks = new Set<string>();

    constructor(private readonly turn: Turn) {}

    runningTaskId(toolCallId: string): string | undefined {
        if (this.finishedTasks.has(toolCallId)) return undefined;
        for (const [taskId, callId] of this.tasks) {
            if (callId === toolCallId) return taskId;
        }
        return undefined;
    }

    accept(message: SDKMessage): void {
        const emit = this.turn.emit;
        if (message.type === "stream_event") {
            const { event, parent_tool_use_id: parent } = message;
            const parentToolCallId = parent ?? undefined;
            if (event.type === "message_start") {
                this.messageIds.set(parent, event.message.id);
            } else if (event.type === "content_block_delta") {
                if (event.delta.type === "text_delta") {
                    const id = this.messageIds.get(parent) ?? "";
                    const block = this.streamTextBlock(parent, id, event.index);
                    block.text += event.delta.text;
                    emit({ type: "assistantTextDelta", text: event.delta.text, messageId: block.messageId, parentToolCallId });
                } else if (event.delta.type === "thinking_delta")
                    emit({ type: "reasoningDelta", text: event.delta.thinking, parentToolCallId });
            }
        } else if (message.type === "assistant") {
            const parent = message.parent_tool_use_id;
            if (!parent && message.message.model) this.model = message.message.model;
            const parentToolCallId = parent ?? undefined;
            for (const block of message.message.content) {
                if (block.type === "text") {
                    const messageId = this.completeTextBlock(parent, message.message.id, block.text);
                    emit({ type: "assistantMessageCompleted", text: block.text, messageId, parentToolCallId });
                }
                if (block.type !== "tool_use" || this.describedCalls.has(block.id)) continue;
                this.describedCalls.add(block.id);
                const input = record(block.input);
                const role = typeof input?.subagent_type === "string" ? input.subagent_type : undefined;
                const description = typeof input?.description === "string" ? input.description : undefined;
                const subagent = block.name === "Agent" || block.name === "Task" ? { role, description } : undefined;
                const background = (block.name === "Bash" || block.name === "PowerShell") && input?.run_in_background === true;
                const task = (block.name === "TaskOutput" || block.name === "TaskStop") && typeof input?.task_id === "string"
                    ? this.tasks.get(input.task_id) : undefined;
                const callParent = parentToolCallId ?? task ?? this.calls.get(block.id)?.parentToolCallId;
                this.describeCall(block.id, block.name, callParent, background, input?.run_in_background === true, subagent,
                    typeof input?.command === "string" ? input.command : description, block.input);
                const todos = input?.todos;
                if (!callParent && block.name === "TodoWrite" && Array.isArray(todos)) {
                    emit({
                        type: "planUpdated", steps: todos.flatMap(item =>
                            typeof item?.content === "string" ? [{ text: item.content, status: planStatus(item.status) }] : []),
                    });
                }
            }
        } else if (message.type === "user" && Array.isArray(message.message.content)) {
            for (const block of message.message.content) {
                if (block.type !== "tool_result") continue;
                const call = this.calls.get(block.tool_use_id);
                const parentToolCallId = message.parent_tool_use_id ?? call?.parentToolCallId;
                if (this.finishedTasks.has(block.tool_use_id)) continue;
                if (call?.asynchronous && !block.is_error) {
                    emit({ type: "toolCallUpdated", toolCallId: block.tool_use_id, status: "running", parentToolCallId,
                        message: contentOutputs(block.content)?.find(output => output.type === "text")?.text });
                    continue;
                }
                emit({
                    type: "toolCallCompleted", toolCallId: block.tool_use_id,
                    status: block.is_error ? "failed" : "completed", outputs: contentOutputs(block.content), parentToolCallId,
                });
            }
        } else if (message.type === "tool_progress") {
            emit({ type: "toolCallUpdated", toolCallId: message.tool_use_id, status: "running",
                parentToolCallId: message.parent_tool_use_id ?? undefined });
        } else if (message.type === "system") {
            if (message.subtype === "status" && message.status === "compacting")
                emit({ type: "turnPhaseChanged", phase: "compacting" });
            else if (message.subtype === "compact_boundary") {
                emit({ type: "contextCompacted" });
                emit({ type: "turnPhaseChanged", phase: "thinking" });
            } else if (message.subtype === "status" && message.status === null)
                emit({ type: "turnPhaseChanged", phase: "thinking" });
            else if (message.subtype === "api_retry")
                emit({ type: "turnPhaseChanged", phase: "retrying" });
            this.acceptTask(message);
        }
    }

    private textBucketKey(parent: string | null, messageId: string): string {
        return `${parent ?? ""}\0${messageId}`;
    }

    private streamTextBlock(parent: string | null, messageId: string, streamIndex: number): StreamedTextBlock {
        const key = this.textBucketKey(parent, messageId);
        let blocks = this.textBlocks.get(key);
        if (!blocks) this.textBlocks.set(key, blocks = []);
        let block = blocks.find(candidate => candidate.streamIndex === streamIndex && !candidate.completed);
        if (!block) {
            const prefix = messageId || "stream";
            block = { streamIndex, messageId: `${prefix}:text:${blocks.length}`, text: "", completed: false };
            blocks.push(block);
        }
        return block;
    }

    private completeTextBlock(parent: string | null, messageId: string, text: string): string {
        const key = this.textBucketKey(parent, messageId);
        let blocks = this.textBlocks.get(key);
        if (!blocks && messageId) {
            const fallbackKey = this.textBucketKey(parent, "");
            const fallback = this.textBlocks.get(fallbackKey);
            if (fallback?.some(candidate => !candidate.completed)) {
                blocks = fallback;
                this.textBlocks.set(key, blocks);
                this.textBlocks.delete(fallbackKey);
            }
        }
        if (!blocks) this.textBlocks.set(key, blocks = []);

        // Claude Code can emit one `assistant` event per content block (so content array index is 0)
        // even when `stream_event` used a higher block index after a thinking/tool_use block.
        const matched = blocks.find(candidate => !candidate.completed && candidate.text === text)
            ?? blocks.find(candidate => !candidate.completed)
            ?? blocks.find(candidate => candidate.completed && candidate.text === text);
        if (matched) {
            matched.text = text;
            matched.completed = true;
            return matched.messageId;
        }

        const prefix = messageId || "assistant";
        const created: StreamedTextBlock = { messageId: `${prefix}:text:${blocks.length}`, text, completed: true };
        blocks.push(created);
        return created.messageId;
    }

    private describeCall(toolCallId: string, toolName: string, parentToolCallId: string | undefined, background: boolean,
        asynchronous: boolean, subagent?: { role?: string; description?: string }, title?: string, input?: unknown): void {
        const previous = this.calls.get(toolCallId);
        this.calls.set(toolCallId, { parentToolCallId, background: background || previous?.background === true,
            asynchronous: asynchronous || previous?.asynchronous === true });
        this.turn.emit({ type: "toolCallStarted", toolCallId, toolName, parentToolCallId, input, subagent,
            isBackground: background || undefined, backgroundTitle: background ? title : undefined });
    }

    private acceptTask(message: Extract<SDKMessage, { type: "system" }>): void {
        if (message.subtype === "task_started") {
            const subagent = message.task_type === "local_agent" || message.subagent_type !== undefined;
            const shell = message.task_type === "local_bash";
            if (message.ambient || (!subagent && !shell)) return;
            const id = message.tool_use_id ?? message.task_id;
            const background = shell && message.is_backgrounded !== false;
            this.tasks.set(message.task_id, id);
            this.finishedTasks.delete(id);
            const previous = this.calls.get(id);
            if (!previous || (background && !previous.background)) {
                const parent = previous?.parentToolCallId ?? (message.parent_task_id ? this.tasks.get(message.parent_task_id) : undefined);
                this.describeCall(id, subagent ? "Agent" : "Bash", parent, background, message.is_backgrounded === true || background,
                    subagent ? { role: message.subagent_type, description: message.description } : undefined, message.description,
                    message.prompt);
            } else if (message.is_backgrounded) {
                previous.asynchronous = true;
            }
        } else if (message.subtype === "task_progress" || message.subtype === "task_notification") {
            const toolCallId = this.tasks.get(message.task_id) ?? message.tool_use_id;
            if (!toolCallId || !this.calls.has(toolCallId)) return;
            const parentToolCallId = this.calls.get(toolCallId)?.parentToolCallId;
            if (message.subtype === "task_progress" && !this.finishedTasks.has(toolCallId)) {
                this.turn.emit({ type: "toolCallUpdated", toolCallId, status: "running", parentToolCallId,
                    message: message.summary ?? message.last_tool_name });
            } else if (message.subtype === "task_notification") {
                this.finishedTasks.add(toolCallId);
                this.turn.emit({ type: "toolCallCompleted", toolCallId, parentToolCallId,
                    status: message.status === "stopped" ? "cancelled" : message.status, outputs: contentOutputs(message.summary) });
            }
        }
    }
}
