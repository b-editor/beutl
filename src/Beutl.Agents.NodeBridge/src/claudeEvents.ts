import { type SDKMessage } from "@anthropic-ai/claude-agent-sdk/core";
import { type Turn, contentOutputs, planStatus, record } from "./protocol.js";

export function fallbackContextWindow(model?: string): number {
    return /\[1m\]|(?:opus|sonnet)-4-[6-9]\b|(?:opus|sonnet|fable|mythos)-5\b|haiku-5-5\b|mythos-preview/i.test(model ?? "")
        ? 1_000_000 : 200_000;
}

export function claudeTokenUsage(usage?: { input_tokens?: number; output_tokens?: number;
    cache_read_input_tokens?: number | null; cache_creation_input_tokens?: number | null }) {
    const cachedInputTokens = usage?.cache_read_input_tokens ?? undefined;
    // Fresh input, cache reads, and cache writes all occupy the context window.
    const inputTokens = usage?.input_tokens === undefined ? undefined
        : usage.input_tokens + (cachedInputTokens ?? 0) + (usage.cache_creation_input_tokens ?? 0);
    return { inputTokens, outputTokens: usage?.output_tokens, cachedInputTokens };
}

export interface StreamedTextBlock {
    readonly streamIndex?: number;
    readonly messageId: string;
    text: string;
    completed: boolean;
}

export class ClaudeEvents {
    model?: string;
    private readonly messageIds = new Map<string | null, string>();
    private readonly textBlocks = new Map<string, StreamedTextBlock[]>();
    private readonly calls = new Map<string, { parentToolCallId?: string; background: boolean; asynchronous: boolean }>();
    private readonly describedCalls = new Set<string>();
    private readonly tasks = new Map<string, string>();
    private readonly finishedTasks = new Set<string>();

    constructor(private readonly turn: Turn, private readonly contextWindow: (model?: string) => number,
        private readonly onManualCompact?: () => void) {}

    private reportContext(usage: Parameters<typeof claudeTokenUsage>[0], model?: string): void {
        this.model = model || this.model;
        const counts = claudeTokenUsage(usage);
        if (this.onManualCompact || counts.inputTokens === undefined) return;
        this.turn.reportContextUsage({ ...counts, usedTokens: counts.inputTokens + (counts.outputTokens ?? 0),
            contextWindowTokens: this.contextWindow(this.model) });
    }

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
                if (!parent) this.reportContext(event.message.usage, event.message.model);
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
            if (!parent) this.reportContext(message.message.usage, message.message.model);
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
                if (message.compact_metadata.trigger === "manual") this.onManualCompact?.();
                this.turn.reportContextUsage({ contextWindowTokens: this.contextWindow(this.model) });
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
