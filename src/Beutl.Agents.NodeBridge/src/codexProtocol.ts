import { createInterface } from "node:readline";
import { type ProviderProcess } from "./process.js";
import { errorMessage } from "./protocol.js";

// Subset of the v2 protocol used by the bridge, checked against codex app-server generate-ts.
type RequestId = string | number;
type ApprovalPolicy = "untrusted" | "on-request" | "never";
type SandboxMode = "read-only" | "workspace-write" | "danger-full-access";
type SandboxPolicy =
    | { type: "readOnly"; networkAccess: boolean }
    | { type: "workspaceWrite"; writableRoots: readonly string[]; networkAccess: boolean; excludeTmpdirEnvVar: boolean; excludeSlashTmp: boolean }
    | { type: "dangerFullAccess" };
interface ThreadOptions { cwd: string; model?: string; approvalPolicy: ApprovalPolicy; sandbox: SandboxMode }

interface CodexModel {
    id: string;
    model: string;
    displayName: string;
    supportedReasoningEfforts: { reasoningEffort: string }[];
    defaultReasoningEffort?: string;
    isDefault?: boolean;
}

export interface CodexTurn {
    id: string;
    status: "completed" | "interrupted" | "failed" | "inProgress";
    error?: { message: string } | null;
    durationMs?: number | null;
}

export interface CodexTokenCounts {
    inputTokens: number;
    outputTokens: number;
    cachedInputTokens: number;
}

interface TurnRef { threadId: string; turnId: string }
interface ParentRef { parentToolCallId?: string | null }
interface ItemRef extends TurnRef, ParentRef { itemId: string }
type ToolState = "inProgress" | "completed" | "failed" | "declined" | "interrupted";

export interface CodexSkill {
    name: string;
    description: string;
    shortDescription?: string | null;
    path: string;
    scope?: string;
    enabled?: boolean;
}

export type CodexInput = { type: "text"; text: string; text_elements: [] } | { type: "skill"; name: string; path: string };

export type CodexItem = ParentRef & (
    | { type: "userMessage" | "reasoning" | "contextCompaction"; id: string }
    | { type: "agentMessage" | "plan"; id: string; text: string }
    | { type: "commandExecution"; id: string; command: string; status: ToolState; aggregatedOutput?: string; isBackground?: boolean; background?: boolean }
    | { type: "fileChange"; id: string; status: ToolState; changes: { path: string }[] }
    | { type: "mcpToolCall"; id: string; server: string; tool: string; arguments?: unknown; status: ToolState; result?: { content?: unknown[] } }
    | { type: "dynamicToolCall"; id: string; tool: string; arguments?: unknown; status: ToolState; contentItems?: unknown[] }
    | { type: "imageGeneration"; id: string; status: string; savedPath?: string; result?: string }
    | { type: "collabAgentToolCall"; id: string; tool: string; status: ToolState; senderThreadId?: string;
        receiverThreadIds?: string[]; prompt?: string | null; model?: string | null; agentType?: string | null;
        agentsStates?: Record<string, { status: string; message?: string | null }> });

type Notifications = {
    "turn/started": { threadId: string; turn: CodexTurn };
    "turn/completed": { threadId: string; turn: CodexTurn };
    "item/started": TurnRef & ParentRef & { item: CodexItem };
    "item/updated": TurnRef & ParentRef & { item: CodexItem };
    "item/completed": TurnRef & ParentRef & { item: CodexItem };
    "item/agentMessage/delta": ItemRef & { delta: string };
    "item/reasoning/textDelta": ItemRef & { delta: string };
    "item/reasoning/summaryTextDelta": ItemRef & { delta: string };
    "item/commandExecution/outputDelta": ItemRef & { delta: string };
    "item/fileChange/outputDelta": ItemRef & { delta: string };
    "item/mcpToolCall/progress": ItemRef & { message: string };
    "turn/plan/updated": TurnRef & { plan: { step: string; status: string }[]; explanation?: string | null };
    "thread/tokenUsage/updated": TurnRef & { tokenUsage: { total: CodexTokenCounts; last: CodexTokenCounts } };
    "thread/compacted": TurnRef;
    "model/rerouted": TurnRef & { toModel: string };
    "error": TurnRef & { error: { message: string }; willRetry: boolean };
};

type InboundRequests = {
    "item/commandExecution/requestApproval": ItemRef & { command?: string | null; reason?: string | null; availableDecisions?: unknown[] | null };
    "item/fileChange/requestApproval": ItemRef & { reason?: string | null };
    "item/permissions/requestApproval": ItemRef & { reason?: string | null; permissions: Record<string, unknown> };
    "item/tool/requestUserInput": ItemRef & {
        questions: { id: string; question: string; header?: string; isOther?: boolean; isSecret?: boolean;
            options?: { label: string; description?: string }[] | null }[];
    };
};

export type CodexNotification = { [K in keyof Notifications]: { method: K; params: Notifications[K] } }[keyof Notifications];
export type CodexRequest = { [K in keyof InboundRequests]: { method: K; params: InboundRequests[K] } }[keyof InboundRequests];

interface Requests {
    initialize: [{ clientInfo: { name: string; title: string; version: string }; capabilities: { experimentalApi: boolean; requestAttestation: boolean } }, unknown];
    "thread/start": [ThreadOptions, { thread: { id: string }; model?: string }];
    "thread/resume": [ThreadOptions & { threadId: string; excludeTurns: boolean }, { thread: { id: string }; model?: string }];
    "model/list": [{ cursor?: string; includeHidden: boolean }, { data: CodexModel[]; nextCursor?: string | null }];
    "skills/list": [{ cwds: string[] }, { data: { skills: CodexSkill[] }[] }];
    "turn/start": [{
        threadId: string; input: CodexInput[];
        cwd: string; approvalPolicy: ApprovalPolicy; sandboxPolicy: SandboxPolicy; model?: string; effort?: string;
    }, { turn: CodexTurn }];
    "turn/steer": [{ threadId: string; expectedTurnId: string; input: CodexInput[] }, { turnId: string }];
    "turn/interrupt": [TurnRef, object];
}

interface Frame {
    id?: RequestId;
    method?: string;
    params?: unknown;
    result?: unknown;
    error?: { code: number; message: string };
}

export class CodexRpc {
    private id = 0;
    private readonly pending = new Map<RequestId, PromiseWithResolvers<unknown>>();
    private readonly lines;
    private terminalError?: Error;

    constructor(
        private readonly process: ProviderProcess,
        notify: (notification: CodexNotification) => void,
        onRequest: (request: CodexRequest) => Promise<unknown>,
        onDisconnect: (message: string) => void,
    ) {
        this.lines = createInterface({ input: process.child.stdout, crlfDelay: Infinity });
        this.lines.on("line", (line: string) => {
            try {
                const frame = JSON.parse(line) as Frame;
                if (frame.method) {
                    if (frame.id !== undefined) {
                        const { id, method, params } = frame;
                        void onRequest({ method, params } as CodexRequest)
                            .then(result => this.send({ id, result }), error => {
                                this.send({ id, error: { code: -32601, message: errorMessage(error) } });
                            });
                    } else {
                        notify({ method: frame.method, params: frame.params } as CodexNotification);
                    }
                } else if (frame.id !== undefined) {
                    const request = this.pending.get(frame.id);
                    if (frame.error) request?.reject(new Error(frame.error.message));
                    else request?.resolve(frame.result);
                }
            } catch (error) {
                this.disconnect(errorMessage(error), onDisconnect);
            }
        });
        this.lines.once("close", () => this.disconnect("Codex app-server closed stdout.", onDisconnect));
        this.lines.on("error", error => this.disconnect(errorMessage(error), onDisconnect));
        void process.closed.then(message => this.disconnect(message, onDisconnect));
    }

    async request<K extends keyof Requests>(method: K, params: Requests[K][0]): Promise<Requests[K][1]> {
        if (this.terminalError) throw this.terminalError;
        const id = ++this.id;
        const result = Promise.withResolvers<unknown>();
        this.pending.set(id, result);
        try {
            this.send({ id, method, params });
            return await result.promise as Requests[K][1];
        } finally {
            this.pending.delete(id);
        }
    }

    initialized(): void {
        this.send({ method: "initialized" });
    }

    private send(frame: Frame): void {
        // Codex app-server uses the JSON-RPC shape without a jsonrpc field.
        if (!this.terminalError) this.process.child.stdin.write(JSON.stringify(frame) + "\n");
    }

    private disconnect(message: string, onDisconnect: (message: string) => void): void {
        if (this.terminalError) return;
        this.terminalError = new Error(message);
        for (const promise of this.pending.values()) promise.reject(this.terminalError);
        onDisconnect(message);
    }
}
