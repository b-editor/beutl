import { type CodexItem, type CodexNotification, type CodexTokenCounts, type CodexTurn } from "./codexProtocol.js";
import { type ContextUsage, type ToolStatus, type Turn, type TurnResult, contentOutputs, planStatus } from "./protocol.js";

export interface ActiveCodexTurn {
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

export interface CodexEventContext {
    threadId?: string;
    defaultModel?: string;
    active?: ActiveCodexTurn;
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

export class CodexEvents {
    private tokenTotal?: CodexTokenCounts;
    private contextWindow?: number;

    notify(notification: CodexNotification, context: CodexEventContext): void {
        const { active, threadId, defaultModel } = context;
        const params = notification.params;
        if (!params?.threadId) return;
        const root = params.threadId === threadId;
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
                    turn.reportUsage({ model: defaultModel });
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
                if (!turn.usage.model) turn.reportUsage({ model: defaultModel });
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

    private item(active: ActiveCodexTurn, item: CodexItem, finished: boolean, parentToolCallId?: string): void {
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

    private collabItem(active: ActiveCodexTurn, item: Extract<CodexItem, { type: "collabAgentToolCall" }>, finished: boolean,
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
            const [, state] = states[0];
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

    private updateAgent(active: ActiveCodexTurn, toolCallId: string, status: string, message?: string): void {
        const parentToolCallId = active.collabParents.get(toolCallId);
        const normalized = toolStatus(status);
        if (normalized === "running")
            active.turn.emit({ type: "toolCallUpdated", toolCallId, status: normalized, parentToolCallId, message });
        else
            active.turn.emit({ type: "toolCallCompleted", toolCallId, status: normalized, parentToolCallId, outputs: contentOutputs(message) });
    }
}
